using System.Net;
using TraceCapsule.Core.Recording;
using TraceCapsule.Http;

namespace TraceCapsule.UnitTests;

file sealed class FakeInnerHandler(HttpResponseMessage response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(response);
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
}
