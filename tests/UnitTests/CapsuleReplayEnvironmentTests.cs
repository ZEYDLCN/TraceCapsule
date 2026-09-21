using Microsoft.Extensions.DependencyInjection;
using TraceCapsule.Core.Model;
using TraceCapsule.Http;

namespace TraceCapsule.UnitTests;

public class CapsuleReplayEnvironmentTests
{
    private static Capsule CapsuleWith(params (string Dependency, string Url, int Status, string Body)[] calls) => new()
    {
        Metadata = new CapsuleMetadata { TraceId = "t" },
        ExternalHttpCalls = calls.Select(c => new ExternalHttpCallRecord
        {
            DependencyName = c.Dependency, Method = "GET", Url = c.Url, ResponseStatusCode = c.Status, ResponseBody = c.Body,
        }).ToList(),
    };

    [Fact]
    public async Task Registers_a_named_client_per_recorded_dependency_that_replays_its_own_calls()
    {
        var capsule = CapsuleWith(
            ("fraud-api", "https://fraud-api.example/check", 200, """{"riskScore":81}"""),
            ("balance-api", "https://balance-api.example/lookup", 200, """{"balance":500}"""));

        var services = new ServiceCollection();
        services.AddTraceCapsuleReplayEnvironment(capsule);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var fraudClient = factory.CreateClient("fraud-api");
        var fraudBody = await (await fraudClient.GetAsync("https://fraud-api.example/check")).Content.ReadAsStringAsync();
        using var balanceClient = factory.CreateClient("balance-api");
        var balanceBody = await (await balanceClient.GetAsync("https://balance-api.example/lookup")).Content.ReadAsStringAsync();

        Assert.Equal("""{"riskScore":81}""", fraudBody);
        Assert.Equal("""{"balance":500}""", balanceBody);
    }

    [Fact]
    public async Task Treats_dependency_names_that_only_differ_by_case_as_the_same_client()
    {
        var capsule = CapsuleWith(
            ("fraud-api", "https://fraud-api.example/check?id=1", 200, "first"),
            ("Fraud-API", "https://fraud-api.example/check?id=2", 200, "second"));

        var services = new ServiceCollection();
        services.AddTraceCapsuleReplayEnvironment(capsule);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient("fraud-api");

        var first = await (await client.GetAsync("https://fraud-api.example/check?id=1")).Content.ReadAsStringAsync();
        var second = await (await client.GetAsync("https://fraud-api.example/check?id=2")).Content.ReadAsStringAsync();

        Assert.Equal("first", first);
        Assert.Equal("second", second);
    }
}
