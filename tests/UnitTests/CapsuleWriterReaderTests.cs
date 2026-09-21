using TraceCapsule.Core.Capsules;
using TraceCapsule.Core.Model;

namespace TraceCapsule.UnitTests;

public class CapsuleWriterReaderTests
{
    private static Capsule BuildSampleCapsule() => new()
    {
        Metadata = new CapsuleMetadata
        {
            TraceId = "7f92bd12",
            Timestamp = new DateTimeOffset(2026, 9, 21, 10, 14, 33, TimeSpan.Zero),
            Request = new RequestInfo { Method = "POST", Path = "/api/transfers" },
            Services = ["transfer-service", "fraud-service"],
            Environment = new EnvironmentInfo { AppVersion = "2.3.1" },
        },
        Request = new HttpRequestRecord { Method = "POST", Path = "/api/transfers", Body = """{"amount":5000}""" },
        Response = new HttpResponseRecord { StatusCode = 500 },
        Trace =
        [
            new SpanRecord { SpanId = "s1", Name = "POST /transfer", ServiceName = "transfer-service", DurationMs = 1321 },
        ],
        ExternalHttpCalls =
        [
            new ExternalHttpCallRecord { DependencyName = "balance-api", Method = "GET", Url = "https://balance/x", ResponseStatusCode = 504, DurationMs = 900 },
        ],
        Events = [new QueueEventRecord { EventType = "TransferRequested", MessageId = "msg-1", Direction = QueueEventDirection.Publish }],
        Exceptions = [new ExceptionRecord { Type = "TimeoutException", Message = "boom", SpanId = "s1" }],
        Timing = new TimingRecord
        {
            StartedAt = new DateTimeOffset(2026, 9, 21, 10, 14, 33, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 9, 21, 10, 14, 34, 321, TimeSpan.Zero),
            TotalDurationMs = 1321,
        },
    };

    [Fact]
    public async Task Round_trip_through_a_stream_preserves_all_sections()
    {
        var original = BuildSampleCapsule();

        using var buffer = new MemoryStream();
        await CapsuleWriter.WriteAsync(original, buffer);
        buffer.Position = 0;
        var loaded = await CapsuleReader.ReadAsync(buffer);

        Assert.Equal(original.Metadata.TraceId, loaded.Metadata.TraceId);
        Assert.Equal(original.Metadata.Services, loaded.Metadata.Services);
        Assert.Equal(original.Request!.Body, loaded.Request!.Body);
        Assert.Equal(original.Response!.StatusCode, loaded.Response!.StatusCode);
        Assert.Single(loaded.Trace);
        Assert.Equal(original.Trace[0].SpanId, loaded.Trace[0].SpanId);
        Assert.Single(loaded.ExternalHttpCalls);
        Assert.Equal(504, loaded.ExternalHttpCalls[0].ResponseStatusCode);
        Assert.Single(loaded.Events);
        Assert.Equal(QueueEventDirection.Publish, loaded.Events[0].Direction);
        Assert.Single(loaded.Exceptions);
        Assert.Equal("TimeoutException", loaded.Exceptions[0].Type);
        Assert.Equal(1321, loaded.Timing!.TotalDurationMs);
    }

    [Fact]
    public async Task Round_trip_through_a_file_path_works()
    {
        var original = BuildSampleCapsule();
        var path = Path.Combine(Path.GetTempPath(), $"tracecapsule-test-{Guid.NewGuid():N}.capsule");
        try
        {
            await CapsuleWriter.WriteAsync(original, path);
            var loaded = await CapsuleReader.ReadAsync(path);
            Assert.Equal(original.Metadata.TraceId, loaded.Metadata.TraceId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Sections_that_were_never_set_come_back_empty_not_null()
    {
        var minimal = new Capsule { Metadata = new CapsuleMetadata { TraceId = "abc" } };
        using var buffer = new MemoryStream();
        await CapsuleWriter.WriteAsync(minimal, buffer);
        buffer.Position = 0;
        var loaded = await CapsuleReader.ReadAsync(buffer);

        Assert.Equal("abc", loaded.Metadata.TraceId);
        Assert.Null(loaded.Request);
        Assert.Null(loaded.Response);
        Assert.Null(loaded.Timing);
        Assert.Empty(loaded.Trace);
        Assert.Empty(loaded.ExternalHttpCalls);
        Assert.Empty(loaded.Events);
        Assert.Empty(loaded.Exceptions);
    }
}
