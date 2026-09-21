using System.Net;
using TraceCapsule.Core.Fault;
using TraceCapsule.Core.Recording;
using TraceCapsule.Http;

namespace TraceCapsule.UnitTests;

file sealed class FakeInnerHandler(HttpResponseMessage response) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(response);
    }
}

file sealed class ThrowingInnerHandler(Exception exception) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw exception;
}

public class RecordingHttpMessageHandlerTests
{
    [Fact]
    public async Task Does_nothing_special_when_no_capsule_is_being_recorded()
    {
        var handler = new RecordingHttpMessageHandler("fraud-api") { InnerHandler = new FakeInnerHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }) };
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://fraud-api.example/check");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Records_the_call_into_the_ambient_context_and_still_returns_a_readable_response()
    {
        using var scope = CapsuleRecordingContext.Begin("trace-1", null, out var recording);
        var handler = new RecordingHttpMessageHandler("fraud-api")
        {
            InnerHandler = new FakeInnerHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"riskScore":81}""") }),
        };
        using var client = new HttpClient(handler);

        var response = await client.PostAsync("https://fraud-api.example/check", new StringContent("""{"amount":5000}"""));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal("""{"riskScore":81}""", body);
        var recorded = Assert.Single(recording.ExternalHttpCalls);
        Assert.Equal("fraud-api", recorded.DependencyName);
        Assert.Equal("POST", recorded.Method);
        Assert.Equal(200, recorded.ResponseStatusCode);
        Assert.Equal("""{"riskScore":81}""", recorded.ResponseBody);
        Assert.Equal("""{"amount":5000}""", recorded.RequestBody);
    }

    [Fact]
    public async Task Forwards_the_session_id_onto_the_outbound_request()
    {
        using var scope = CapsuleRecordingContext.Begin("trace-1", "session-1", out _);
        var inner = new FakeInnerHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var handler = new RecordingHttpMessageHandler("fraud-api") { InnerHandler = inner };
        using var client = new HttpClient(handler);

        await client.GetAsync("https://fraud-api.example/check");

        Assert.Equal("session-1", inner.LastRequest!.Headers.GetValues("X-TraceCapsule-Session").Single());
    }

    [Fact]
    public async Task Forwards_active_fault_injection_instructions_onto_the_outbound_request()
    {
        using var faultScope = FaultInjectionContext.Begin(new FaultInjectionOptions().With("payment-api", new FaultSpec { ExtraLatencyMs = 3000 }));
        var inner = new FakeInnerHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var handler = new RecordingHttpMessageHandler("fraud-api") { InnerHandler = inner };
        using var client = new HttpClient(handler);

        await client.GetAsync("https://fraud-api.example/check");

        Assert.Equal("payment-api=3000", inner.LastRequest!.Headers.GetValues("X-TraceCapsule-Fault-Latency").Single());
    }

    [Fact]
    public async Task Records_a_timeout_as_a_call_with_no_response_and_still_rethrows()
    {
        using var scope = CapsuleRecordingContext.Begin("trace-1", null, out var recording);
        var handler = new RecordingHttpMessageHandler("payment-api")
        {
            InnerHandler = new ThrowingInnerHandler(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout")),
        };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.PostAsync("https://payment-api.example/reserve", new StringContent("""{"amount":5000}""")));

        var recorded = Assert.Single(recording.ExternalHttpCalls);
        Assert.Equal("payment-api", recorded.DependencyName);
        Assert.Equal(0, recorded.ResponseStatusCode);
        Assert.Contains("TaskCanceledException", recorded.ResponseBody);
        Assert.Equal("""{"amount":5000}""", recorded.RequestBody);
    }
}
