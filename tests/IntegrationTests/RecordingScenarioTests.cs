using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TraceCapsule.AspNetCore;
using TraceCapsule.Cli.Compare;
using TraceCapsule.Cli.Replay;
using TraceCapsule.Core;
using TraceCapsule.Core.Capsules;
using TraceCapsule.Core.Fault;
using TraceCapsule.Core.Model;
using TraceCapsule.Http;
using TraceCapsule.RabbitMQ;

namespace TraceCapsule.IntegrationTests;

public sealed class RecordingScenarioTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tracecapsule-scenarios-{Guid.NewGuid():N}");
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddTraceCapsule(options =>
        {
            options.OutputDirectory = _directory;
            options.MaxCapturedBodyBytes = 256;
            options.CapturePolicy.SamplingRate = 0;
            options.CapturePolicy.LatencyThresholdMs = double.MaxValue;
        });
        _app = builder.Build();
        _app.UseTraceCapsule();
        _app.MapPost("/echo", async context =>
        {
            context.Response.ContentType = context.Request.ContentType;
            await context.Request.Body.CopyToAsync(context.Response.Body);
        });
        _app.MapGet("/status/{code:int}", (int code) => Results.StatusCode(code));
        _app.MapGet("/annotated", () => Results.Ok()).WithMetadata(new TraceCapsuleAttribute());
        _app.MapGet("/dependencies", async () =>
        {
            using var client = new HttpClient(new RecordingHttpMessageHandler("local-dependency") { InnerHandler = new StubDependency() });
            using var request = new HttpRequestMessage(HttpMethod.Post, "http://dependency.test/check");
            request.Headers.Add("Authorization", "Bearer outbound-secret");
            request.Content = new StringContent("{\"password\":\"request-secret\"}", Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            QueueEventRecorder.RecordPublish("test", "events", "created", "Created", "m1", "c1",
                Encoding.UTF8.GetBytes("{\"token\":\"queue-secret\",\"padding\":\"" + new string('x', 70000) + "\"}"),
                new Dictionary<string, string> { ["Authorization"] = "queue-header-secret" });
            return Results.Ok();
        });
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    }

    [Fact]
    public async Task Successful_unsampled_request_does_not_create_a_capsule()
    {
        using var response = await _client.PostAsJsonAsync("/echo", new { ok = true });
        response.EnsureSuccessStatusCode();
        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(404, false)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    public async Task Error_capture_obeys_http_status(int status, bool captured)
    {
        using var response = await _client.GetAsync($"/status/{status}");
        Assert.Equal(status, (int)response.StatusCode);
        if (captured) Assert.Equal(status, (await ReadOnlyAsync()).Response!.StatusCode);
        else Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task Large_json_is_redacted_before_truncation_and_the_client_body_is_unchanged()
    {
        Capture();
        var body = "{\"password\":\"large-body-secret\",\"padding\":\"" + new string('x', 2000) + "\"}";
        using var response = await _client.PostAsync("/echo", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
        var capsule = await ReadOnlyAsync();
        Assert.DoesNotContain("large-body-secret", capsule.Request!.Body);
        Assert.DoesNotContain("large-body-secret", capsule.Response!.Body);
        Assert.EndsWith("...(truncated)", capsule.Request.Body);
    }

    [Fact]
    public async Task Dependency_and_queue_secrets_are_redacted_in_the_persisted_capsule()
    {
        Capture();
        using var response = await _client.GetAsync("/dependencies");
        response.EnsureSuccessStatusCode();
        var capsule = await ReadOnlyAsync();
        var call = Assert.Single(capsule.ExternalHttpCalls);
        Assert.Equal("***", call.RequestHeaders["Authorization"][0]);
        Assert.DoesNotContain("request-secret", call.RequestBody);
        Assert.DoesNotContain("response-secret", call.ResponseBody);
        Assert.Equal("***", call.ResponseHeaders["Set-Cookie"][0]);
        var message = Assert.Single(capsule.Events);
        Assert.DoesNotContain("queue-secret", message.Payload);
        Assert.Equal("***", message.Headers["Authorization"]);
    }

    [Fact]
    public async Task Concurrent_requests_keep_bodies_sessions_and_traces_isolated()
    {
        Capture();
        await Task.WhenAll(Enumerable.Range(0, 30).Select(async index =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/echo?index=" + index);
            request.Headers.Add(TraceCapsuleHeaders.Session, "session-" + index);
            request.Content = JsonContent.Create(new { index, password = "concurrent-secret-" + index });
            using var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            Assert.Equal("session-" + index, response.Headers.GetValues(TraceCapsuleHeaders.Session).Single());
        }));
        var capsules = await ReadCapsulesAsync(30);
        var files = Directory.GetFiles(_directory, "*.capsule");
        Assert.Equal(30, files.Length);
        Assert.Equal(30, capsules.Select(c => c.Metadata.TraceId).Distinct().Count());
        foreach (var capsule in capsules)
        {
            var index = int.Parse(capsule.Metadata.SessionId!["session-".Length..]);
            Assert.Equal("?index=" + index, capsule.Request!.QueryString);
            Assert.Contains("\"index\":" + index, capsule.Request.Body);
            Assert.DoesNotContain("concurrent-secret", capsule.Request.Body);
            Assert.Equal(capsule.Request.Body, capsule.Response!.Body);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Merhaba İstanbul 🌍")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"nested\":[{\"PASSWORD\":\"nested-secret\"}]}")]
    public async Task Empty_unicode_array_and_nested_bodies_survive_recording(string body)
    {
        Capture();
        using var response = await _client.PostAsync("/echo", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
        var capsule = await ReadOnlyAsync();
        Assert.DoesNotContain("nested-secret", capsule.Request!.Body ?? "");
        if (!body.Contains("nested-secret")) Assert.Equal(body.Length == 0 ? null : body, capsule.Request.Body);
    }

    [Fact]
    public async Task Binary_body_is_not_written_as_raw_text()
    {
        Capture();
        byte[] bytes = [0, 1, 2, 255, 128];
        using var response = await _client.PostAsync("/echo", new ByteArrayContent(bytes));
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("<binary, 5 bytes>", (await ReadOnlyAsync()).Request!.Body);
    }

    [Fact]
    public async Task Storage_failure_does_not_break_the_application_response()
    {
        Capture();
        await File.WriteAllTextAsync(_directory, "A file blocks directory creation.");
        using var response = await _client.PostAsJsonAsync("/echo", new { ok = true });
        response.EnsureSuccessStatusCode();
        Assert.Contains("true", await response.Content.ReadAsStringAsync());
    }

    private void Capture() => _client.DefaultRequestHeaders.Add(TraceCapsuleHeaders.ManualCapture, "true");

    [Fact]
    public async Task Real_recording_can_be_loaded_replayed_and_compared()
    {
        Capture();
        using var response = await _client.PostAsJsonAsync("/echo?language=tr", new { text = "İstanbul", password = "recording-secret" });
        response.EnsureSuccessStatusCode();
        var original = await ReadOnlyAsync();
        Assert.Contains("charset=utf-8", original.Request!.ContentType);
        var replay = await CapsuleReplayer.ReplayAsync(original, _client.BaseAddress!, FaultInjectionOptions.None);
        Assert.Empty(replay.Exceptions);
        Assert.Equal(original.Response!.Body, replay.Response!.Body);
        Assert.True(CapsuleComparer.Compare(original, replay).AllMatch);
        await ReadCapsulesAsync(2);
    }

    [Fact]
    public async Task Attribute_requirement_bypasses_unannotated_endpoints_even_with_manual_capture()
    {
        _app.Services.GetRequiredService<IOptions<TraceCapsuleOptions>>().Value.RequireAttribute = true;
        Capture();
        using var skipped = await _client.GetAsync("/status/200");
        skipped.EnsureSuccessStatusCode();
        Assert.False(Directory.Exists(_directory));
        using var captured = await _client.GetAsync("/annotated");
        captured.EnsureSuccessStatusCode();
        Assert.Equal("/annotated", (await ReadOnlyAsync()).Request!.Path);
    }

    [Fact]
    public async Task Latency_policy_captures_without_sampling_or_manual_header()
    {
        _app.Services.GetRequiredService<IOptions<TraceCapsuleOptions>>().Value.CapturePolicy.LatencyThresholdMs = 0;
        using var response = await _client.GetAsync("/status/200");
        response.EnsureSuccessStatusCode();
        Assert.Equal(200, (await ReadOnlyAsync()).Response!.StatusCode);
    }

    [Fact]
    public async Task Explicitly_disabled_redaction_preserves_the_body()
    {
        _app.Services.GetRequiredService<IOptions<TraceCapsuleOptions>>().Value.EnableRedaction = false;
        Capture();
        using var response = await _client.PostAsJsonAsync("/echo", new { password = "intentional-plaintext" });
        response.EnsureSuccessStatusCode();
        Assert.Contains("intentional-plaintext", (await ReadOnlyAsync()).Request!.Body);
    }
    private async Task<Capsule> ReadOnlyAsync() => Assert.Single(await ReadCapsulesAsync(1));

    private async Task<Capsule[]> ReadCapsulesAsync(int count)
    {
        // A real HTTP client can receive the response before the middleware has persisted
        // the capsule. Wait for complete, readable artifacts instead of racing the writer.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var files = Directory.Exists(_directory) ? Directory.GetFiles(_directory, "*.capsule") : [];
            if (files.Length == count)
            {
                try { return await Task.WhenAll(files.Select(file => CapsuleReader.ReadAsync(file))); }
                catch (IOException) { }
                catch (InvalidDataException) { }
            }
            await Task.Delay(25);
        }
        throw new TimeoutException($"Expected {count} complete capsules in {_directory}.");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        if (File.Exists(_directory)) File.Delete(_directory);
    }

    private sealed class StubDependency : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"token\":\"response-secret\"}") };
            response.Headers.Add("Set-Cookie", "session=response-cookie-secret");
            return Task.FromResult(response);
        }
    }
}
