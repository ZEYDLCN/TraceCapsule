using System.Diagnostics;
using System.Net;
using TraceCapsule.Core.Fault;
using TraceCapsule.Core.Model;
using TraceCapsule.Http;

namespace TraceCapsule.UnitTests;

public class ReplayHttpMessageHandlerTests
{
    private static ExternalHttpCallRecord Call(string method, string url, int status, string? body) => new()
    {
        DependencyName = "fraud-api", Method = method, Url = url, ResponseStatusCode = status, ResponseBody = body,
    };

    [Fact]
    public async Task Serves_the_recorded_response_instead_of_calling_the_network()
    {
        var handler = new ReplayHttpMessageHandler([Call("GET", "https://fraud-api.example/check?id=1", 200, """{"riskScore":81}""")], "fraud-api");
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://fraud-api.example/check?id=1");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"riskScore":81}""", body);
    }

    [Fact]
    public async Task Throws_a_clear_error_when_no_recorded_call_matches()
    {
        var handler = new ReplayHttpMessageHandler([], "fraud-api");
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<TraceCapsuleReplayException>(() => client.GetAsync("https://fraud-api.example/check"));
        Assert.Contains("fraud-api", ex.Message);
    }

    [Fact]
    public async Task Ignores_recorded_calls_that_belong_to_a_different_dependency()
    {
        var handler = new ReplayHttpMessageHandler([Call("GET", "https://fraud-api.example/check", 200, "{}")], "different-dependency-name");
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<TraceCapsuleReplayException>(() => client.GetAsync("https://fraud-api.example/check"));
    }

    [Fact]
    public async Task Consumes_matching_calls_in_recorded_order_for_repeated_requests()
    {
        var handler = new ReplayHttpMessageHandler(
        [
            Call("GET", "https://fraud-api.example/check", 200, "first"),
            Call("GET", "https://fraud-api.example/check", 200, "second"),
        ], "fraud-api");
        using var client = new HttpClient(handler);

        var first = await (await client.GetAsync("https://fraud-api.example/check")).Content.ReadAsStringAsync();
        var second = await (await client.GetAsync("https://fraud-api.example/check")).Content.ReadAsStringAsync();

        Assert.Equal("first", first);
        Assert.Equal("second", second);
    }

    [Fact]
    public async Task Applies_injected_latency_for_the_named_dependency()
    {
        var handler = new ReplayHttpMessageHandler([Call("GET", "https://fraud-api.example/check", 200, "{}")], "fraud-api");
        using var client = new HttpClient(handler);
        using var faultScope = FaultInjectionContext.Begin(new FaultInjectionOptions().With("fraud-api", new FaultSpec { ExtraLatencyMs = 150 }));

        var stopwatch = Stopwatch.StartNew();
        await client.GetAsync("https://fraud-api.example/check");
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds >= 150, $"expected at least 150ms, was {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Applies_a_forced_status_code_override()
    {
        var handler = new ReplayHttpMessageHandler([Call("GET", "https://fraud-api.example/check", 200, "{}")], "fraud-api");
        using var client = new HttpClient(handler);
        using var faultScope = FaultInjectionContext.Begin(new FaultInjectionOptions().With("fraud-api", new FaultSpec { ForcedStatusCode = 503 }));

        var response = await client.GetAsync("https://fraud-api.example/check");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
