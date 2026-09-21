using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TraceCapsule.AspNetCore;
using TraceCapsule.Core.Capsules;

namespace TraceCapsule.IntegrationTests;

public sealed class TraceCapsuleMiddlewareTests : IDisposable
{
    private readonly string _capsuleDir = Path.Combine(Path.GetTempPath(), $"tracecapsule-it-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public TraceCapsuleMiddlewareTests()
    {
        Directory.CreateDirectory(_capsuleDir);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.Configure<TraceCapsuleOptions>(o => o.OutputDirectory = _capsuleDir)));
    }

    [Fact]
    public async Task Successful_request_produces_a_capsule_with_redacted_body_and_a_span()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/echo", new { fromAccount = "ACC-102", password = "sup3r-secret" });
        response.EnsureSuccessStatusCode();

        var capsule = await ReadTheOnlyCapsuleAsync();
        Assert.Equal(200, capsule.Response!.StatusCode);
        Assert.Contains("ACC-102", capsule.Request!.Body);
        Assert.DoesNotContain("sup3r-secret", capsule.Request!.Body);
        Assert.NotEmpty(capsule.Trace);
    }

    [Fact]
    public async Task Unhandled_exception_is_recorded_and_still_propagates_as_a_500()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        // The default test server rethrows unhandled exceptions unless configured otherwise;
        // ASP.NET Core's ExceptionHandlerMiddleware isn't in this minimal pipeline, but the
        // TestServer still surfaces a 500 to the HTTP client rather than throwing in-process.
        var response = await client.GetAsync("/boom");

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

        var capsule = await ReadTheOnlyCapsuleAsync();
        var exception = Assert.Single(capsule.Exceptions);
        Assert.Equal("System.InvalidOperationException", exception.Type);
        Assert.Contains("simulated failure", exception.Message);
        // The framework's "turn an unhandled exception into a 500" logic runs outside this
        // middleware's own frame, so context.Response.StatusCode is never actually mutated —
        // the capsule must still reflect the 500 the caller really saw.
        Assert.Equal(500, capsule.Response!.StatusCode);
        Assert.Equal("Error", capsule.Trace[0].Status);
    }

    [Fact]
    public async Task Authorization_header_is_redacted()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer super-secret-token");

        await client.PostAsJsonAsync("/echo", new { ok = true });

        var capsule = await ReadTheOnlyCapsuleAsync();
        Assert.Equal("***", capsule.Request!.Headers["Authorization"][0]);
    }

    private async Task<Core.Model.Capsule> ReadTheOnlyCapsuleAsync()
    {
        var file = Assert.Single(Directory.GetFiles(_capsuleDir, "*.capsule"));
        return await CapsuleReader.ReadAsync(file);
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_capsuleDir)) Directory.Delete(_capsuleDir, recursive: true);
    }
}
