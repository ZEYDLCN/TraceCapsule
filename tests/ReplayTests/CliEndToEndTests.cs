using System.Diagnostics;
using System.IO.Compression;
using TraceCapsule.Cli.Replay;
using TraceCapsule.Core.Capsules;
using TraceCapsule.Core.Model;

namespace TraceCapsule.ReplayTests;

public sealed class CliEndToEndTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tracecapsule cli {Guid.NewGuid():N}");

    public CliEndToEndTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Inspect_html_export_replay_and_compare_work_as_real_cli_processes()
    {
        var path = await WriteAsync("original.capsule", Sample());
        var html = Path.Combine(_directory, "report with spaces.html");
        AssertSuccess(await RunAsync("inspect", path, "--html", html));
        Assert.Contains("<!DOCTYPE html>", await File.ReadAllTextAsync(html));
        var exported = Path.Combine(_directory, "exported.zip");
        AssertSuccess(await RunAsync("export", "--trace", "cli-trace", "--from", _directory, "--to", exported));
        Assert.Equal(await File.ReadAllBytesAsync(path), await File.ReadAllBytesAsync(exported));

        using var server = new FakeHttpServer();
        var serving = server.RespondOnceAsync(200, "{\"ok\":true}");
        var replayPath = Path.Combine(_directory, "replayed.capsule");
        AssertSuccess(await RunAsync("replay", path, "--target-url", server.BaseUrl.ToString(), "--output", replayPath, "--latency", "payment-api=15"));
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("/echo?language=tr", server.LastReceivedPath);
        Assert.Equal("payment-api=15", server.LastReceivedFaultHeader);
        Assert.Equal("{\"ok\":true}", server.LastReceivedBody);
        Assert.Equal(200, (await CapsuleReader.ReadAsync(replayPath)).Response!.StatusCode);
        AssertSuccess(await RunAsync("compare", path, replayPath));
    }

    [Theory]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("text/plain; charset=utf-8")]
    public async Task Replay_accepts_recorded_content_type_parameters(string contentType)
    {
        var capsule = Sample();
        capsule.Request!.ContentType = contentType;
        var path = await WriteAsync("content-type.capsule", capsule);
        using var server = new FakeHttpServer();
        var serving = server.RespondOnceAsync(200, "{}");
        AssertSuccess(await RunAsync("replay", path, "--target-url", server.BaseUrl.ToString()));
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Compare_returns_nonzero_when_status_differs()
    {
        var original = await WriteAsync("original.capsule", Sample());
        var changed = Sample();
        changed.Response!.StatusCode = 503;
        var replay = await WriteAsync("changed.capsule", changed);
        Assert.Equal(1, (await RunAsync("compare", original, replay)).Code);
    }

    [Fact]
    public async Task Merge_keeps_entrypoint_response_and_repeated_merge_does_not_duplicate_calls()
    {
        var first = Sample();
        first.ExternalHttpCalls.Add(new ExternalHttpCallRecord { DependencyName = "payment", Url = "http://payment/", Method = "POST" });
        await WriteAsync("entry.capsule", first);
        var downstream = Sample();
        downstream.Metadata.Timestamp = first.Metadata.Timestamp.AddSeconds(1);
        downstream.Metadata.Services = ["payment"];
        downstream.Response!.Body = "{\"downstream\":true}";
        await WriteAsync("downstream.capsule", downstream);
        AssertSuccess(await RunAsync("merge", "cli-session", "--from", _directory));
        var mergedPath = Path.Combine(_directory, "cli-trace.merged.capsule");
        Assert.Equal(first.Response!.Body, (await CapsuleReader.ReadAsync(mergedPath)).Response!.Body);
        AssertSuccess(await RunAsync("merge", "cli-session", "--from", _directory));
        Assert.Single((await CapsuleReader.ReadAsync(mergedPath)).ExternalHttpCalls);
    }

    [Theory]
    [InlineData("inspect")]
    [InlineData("replay")]
    [InlineData("compare")]
    [InlineData("export")]
    [InlineData("merge")]
    public async Task Missing_required_arguments_return_nonzero(string command) =>
        Assert.NotEqual(0, (await RunAsync(command)).Code);

    [Fact]
    public async Task Missing_and_corrupt_files_return_nonzero()
    {
        Assert.NotEqual(0, (await RunAsync("inspect", Path.Combine(_directory, "missing.capsule"))).Code);
        var corrupt = Path.Combine(_directory, "corrupt.capsule");
        await File.WriteAllTextAsync(corrupt, "not a zip archive");
        Assert.NotEqual(0, (await RunAsync("inspect", corrupt)).Code);
    }

    [Fact]
    public async Task Archive_without_required_metadata_is_rejected()
    {
        var path = Path.Combine(_directory, "empty.capsule");
        using (var stream = File.Create(path))
        using (new ZipArchive(stream, ZipArchiveMode.Create)) { }
        Assert.NotEqual(0, (await RunAsync("inspect", path)).Code);
    }

    [Fact]
    public async Task Export_and_merge_report_an_unknown_execution()
    {
        await WriteAsync("original.capsule", Sample());
        Assert.Equal(1, (await RunAsync("export", "--trace", "missing", "--from", _directory)).Code);
        Assert.Equal(1, (await RunAsync("merge", "missing", "--from", _directory)).Code);
    }

    private static Capsule Sample() => new()
    {
        Metadata = new CapsuleMetadata
        {
            TraceId = "cli-trace", SessionId = "cli-session", Timestamp = DateTimeOffset.UtcNow,
            Services = ["entry"], Request = new RequestInfo { Method = "POST", Path = "/echo" },
        },
        Request = new HttpRequestRecord { Method = "POST", Path = "/echo", QueryString = "?language=tr", Body = "{\"ok\":true}", ContentType = "application/json" },
        Response = new HttpResponseRecord { StatusCode = 200, Body = "{\"ok\":true}" },
    };

    private async Task<string> WriteAsync(string name, Capsule capsule)
    {
        var path = Path.Combine(_directory, name);
        await CapsuleWriter.WriteAsync(capsule, path);
        return path;
    }

    private static async Task<(int Code, string Output)> RunAsync(params string[] args)
    {
        var tests = typeof(CliEndToEndTests).Assembly.Location;
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(tests, ".runtimeconfig.json"), "--depsfile", Path.ChangeExtension(tests, ".deps.json"), typeof(CapsuleReplayer).Assembly.Location }.Concat(args))
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await stdout + await stderr);
    }

    private static void AssertSuccess((int Code, string Output) result) => Assert.True(result.Code == 0, result.Output);
    public void Dispose() => Directory.Delete(_directory, true);
}
