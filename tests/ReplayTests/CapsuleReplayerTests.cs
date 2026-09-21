using TraceCapsule.Cli.Replay;
using TraceCapsule.Core.Fault;
using TraceCapsule.Core.Model;

namespace TraceCapsule.ReplayTests;

public class CapsuleReplayerTests
{
    private static Capsule BuildOriginal() => new()
    {
        Metadata = new CapsuleMetadata
        {
            TraceId = "trace-1",
            Request = new RequestInfo { Method = "POST", Path = "/api/transfers" },
            Services = ["transfer-service"],
        },
        Request = new HttpRequestRecord
        {
            Method = "POST", Path = "/api/transfers", Body = """{"amount":5000}""", ContentType = "application/json",
        },
        Response = new HttpResponseRecord { StatusCode = 500 },
    };

    [Fact]
    public async Task Replays_the_recorded_request_against_the_target_and_captures_the_new_response()
    {
        using var server = new FakeHttpServer();
        var respondTask = server.RespondOnceAsync(201, """{"transferId":"t-1"}""");

        var result = await CapsuleReplayer.ReplayAsync(BuildOriginal(), server.BaseUrl, FaultInjectionOptions.None);
        await respondTask;

        Assert.Equal("/api/transfers", server.LastReceivedPath);
        Assert.Equal("""{"amount":5000}""", server.LastReceivedBody);
        Assert.Equal(201, result.Response!.StatusCode);
        Assert.Contains("t-1", result.Response.Body);
        Assert.NotNull(result.Timing);
        Assert.Empty(result.Exceptions);
    }

    [Fact]
    public async Task Forwards_fault_injection_instructions_as_a_header()
    {
        using var server = new FakeHttpServer();
        var respondTask = server.RespondOnceAsync(200, "{}");

        var faults = new FaultInjectionOptions().With("payment-api", new FaultSpec { ExtraLatencyMs = 3000 });
        await CapsuleReplayer.ReplayAsync(BuildOriginal(), server.BaseUrl, faults);
        await respondTask;

        Assert.Equal("payment-api=3000", server.LastReceivedFaultHeader);
    }

    [Fact]
    public async Task Records_a_connection_failure_as_an_exception_instead_of_throwing()
    {
        var unreachable = new Uri("http://127.0.0.1:1/"); // nothing listens on port 1

        var result = await CapsuleReplayer.ReplayAsync(BuildOriginal(), unreachable, FaultInjectionOptions.None);

        Assert.Null(result.Response);
        Assert.Single(result.Exceptions);
        Assert.NotNull(result.Timing);
    }

    [Fact]
    public async Task Throws_when_the_capsule_has_no_recorded_request()
    {
        var capsule = new Capsule { Metadata = new CapsuleMetadata { TraceId = "t" } };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CapsuleReplayer.ReplayAsync(capsule, new Uri("http://localhost/"), FaultInjectionOptions.None));
    }
}
