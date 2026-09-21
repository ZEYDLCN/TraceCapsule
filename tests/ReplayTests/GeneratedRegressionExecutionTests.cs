using System.Diagnostics;
using System.Security;
using TraceCapsule.Cli.GenerateTest;
using TraceCapsule.Core.Comparison;
using TraceCapsule.Core.Model;

namespace TraceCapsule.ReplayTests;

public sealed class GeneratedRegressionExecutionTests
{
    [Fact]
    public async Task Generated_tests_compile_and_accept_fixes_but_reject_regressions()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tracecapsule generated {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            // Compile the actual output, without downloading packages or rebuilding the
            // solution inside a running test. Use assemblies already loaded by this suite.
            var references = new[]
            {
                typeof(JsonBodyDiff).Assembly.Location,
                typeof(FactAttribute).Assembly.Location,
                typeof(Assert).Assembly.Location,
                typeof(Xunit.Abstractions.ITestOutputHelper).Assembly.Location,
            };
            var referenceXml = string.Join(Environment.NewLine, references.Select(path =>
                $"<Reference Include=\"{Path.GetFileNameWithoutExtension(path)}\"><HintPath>{SecurityElement.Escape(path)}</HintPath></Reference>"));
            await File.WriteAllTextAsync(Path.Combine(directory, "Generated.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <NuGetAudit>false</NuGetAudit>
                  </PropertyGroup>
                  <ItemGroup>{{referenceXml}}</ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(directory, "NuGet.Config"),
                "<configuration><packageSources><clear /></packageSources></configuration>");
            await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), """
                using System.Reflection;
                try
                {
                    var type = Assembly.GetExecutingAssembly().GetType("Generated." + args[0])!;
                    var instance = Activator.CreateInstance(type);
                    await (Task)type.GetMethod("Bug_recorded_no_longer_reproduces")!.Invoke(instance, null)!;
                    return 0;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception);
                    return 1;
                }
                """);

            var cases = new[]
            {
                (Name: "ChangedStatus", Status: 500, ContentType: "application/json", Body: "{}"),
                (Name: "UnchangedStatus", Status: 200, ContentType: "application/json", Body: "{}"),
                (Name: "Utf8Charset", Status: 500, ContentType: "application/json; charset=utf-8", Body: "{\"city\":\"İstanbul 🌍\"}"),
                (Name: "QuotedCharset", Status: 500, ContentType: "text/plain; charset=\"iso-8859-1\"", Body: "café"),
            };
            foreach (var scenario in cases)
            {
                var bug = new Capsule
                {
                    Metadata = new CapsuleMetadata { TraceId = "recorded" },
                    Request = new HttpRequestRecord
                    {
                        Method = "POST", Path = "/echo", QueryString = "?source=generated",
                        ContentType = scenario.ContentType, Body = scenario.Body,
                    },
                    Response = new HttpResponseRecord { StatusCode = scenario.Status, Body = "{\"amount\":1000}" },
                };
                var fixedCapsule = new Capsule
                {
                    Response = new HttpResponseRecord { StatusCode = 200, Body = "{\"amount\":100,\"name\":\"a\",\"timestamp\":\"old\"}" },
                };
                var source = RegressionTestGenerator.Generate(bug, fixedCapsule, "Generated", scenario.Name, ["$.timestamp"], "TRACECAPSULE_GENERATED_TEST_URL");
                await File.WriteAllTextAsync(Path.Combine(directory, scenario.Name + ".cs"), source);
            }

            var build = await RunAsync(directory, null, "build", "Generated.csproj", "-c", "Release", "--configfile", "NuGet.Config", "--nologo");
            Assert.True(build.Code == 0, build.Output);
            var assembly = Path.Combine(directory, "bin", "Release", "net8.0", "Generated.dll");

            foreach (var scenario in cases)
            {
                using var server = new FakeHttpServer();
                var response = server.RespondOnceAsync(200, "{\"amount\":100,\"name\":\"\\u0061\",\"timestamp\":\"new\"}");
                var execution = await RunAsync(directory, server.BaseUrl, assembly, scenario.Name);
                Assert.True(execution.Code == 0, $"{scenario.Name}: {execution.Output}");
                await response.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(scenario.Body, server.LastReceivedBody);
                Assert.Equal(scenario.ContentType, server.LastReceivedContentType);
                Assert.Equal("/echo?source=generated", server.LastReceivedPath);
            }

            // The unchanged-status test must also fail when the original wrong body
            // returns. Merely removing the contradictory status assertion is not enough.
            using var regressedServer = new FakeHttpServer();
            var regressedResponse = regressedServer.RespondOnceAsync(200, "{\"amount\":1000,\"name\":\"a\",\"timestamp\":\"new\"}");
            var regressedExecution = await RunAsync(directory, regressedServer.BaseUrl, assembly, "UnchangedStatus");
            await regressedResponse.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, regressedExecution.Code);
            Assert.Contains("Assert.Empty", regressedExecution.Output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<(int Code, string Output)> RunAsync(string directory, Uri? target, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (target is not null) start.Environment["TRACECAPSULE_GENERATED_TEST_URL"] = target.ToString();
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        return (process.ExitCode, await output + await error);
    }
}
