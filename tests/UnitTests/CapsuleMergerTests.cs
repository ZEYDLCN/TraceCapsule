using TraceCapsule.Core.Capsules;
using TraceCapsule.Core.Model;

namespace TraceCapsule.UnitTests;

public class CapsuleMergerTests
{
    [Fact]
    public void Merge_combines_services_spans_and_events_from_every_part()
    {
        var t0 = new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
        var transferPart = new Capsule
        {
            Metadata = new CapsuleMetadata
            {
                TraceId = "trace-1", SessionId = "session-1", Timestamp = t0,
                Request = new RequestInfo { Method = "POST", Path = "/api/transfers" },
                Services = ["transfer-service"],
            },
            Request = new HttpRequestRecord { Method = "POST", Path = "/api/transfers" },
            Trace = [new SpanRecord { SpanId = "s1", ServiceName = "transfer-service", Name = "POST /transfer" }],
            Timing = new TimingRecord { StartedAt = t0, CompletedAt = t0.AddMilliseconds(1000), TotalDurationMs = 1000 },
        };
        var fraudPart = new Capsule
        {
            Metadata = new CapsuleMetadata
            {
                TraceId = "trace-1", SessionId = "session-1", Timestamp = t0.AddMilliseconds(50),
                Services = ["fraud-service"],
            },
            Trace = [new SpanRecord { SpanId = "s2", ServiceName = "fraud-service", Name = "FraudService.Check" }],
            Events = [new QueueEventRecord { EventType = "TransferRequested", MessageId = "m1", Direction = QueueEventDirection.Consume, Timestamp = t0.AddMilliseconds(60) }],
            Timing = new TimingRecord { StartedAt = t0.AddMilliseconds(50), CompletedAt = t0.AddMilliseconds(400), TotalDurationMs = 350 },
        };

        var merged = CapsuleMerger.Merge([transferPart, fraudPart]);

        Assert.Equal("trace-1", merged.Metadata.TraceId);
        Assert.Equal(["transfer-service", "fraud-service"], merged.Metadata.Services);
        Assert.NotNull(merged.Request);
        Assert.Equal(2, merged.Trace.Count);
        Assert.Single(merged.Events);
        Assert.Equal(t0, merged.Timing!.StartedAt);
        Assert.Equal(1000, merged.Timing.TotalDurationMs);
    }

    [Fact]
    public void Merge_rejects_capsules_from_different_traces()
    {
        var a = new Capsule { Metadata = new CapsuleMetadata { TraceId = "trace-1" } };
        var b = new Capsule { Metadata = new CapsuleMetadata { TraceId = "trace-2" } };

        Assert.Throws<InvalidOperationException>(() => CapsuleMerger.Merge([a, b]));
    }

    [Fact]
    public void Merge_deduplicates_spans_with_the_same_span_id()
    {
        var a = new Capsule
        {
            Metadata = new CapsuleMetadata { TraceId = "t" },
            Trace = [new SpanRecord { SpanId = "dup", Name = "first" }],
        };
        var b = new Capsule
        {
            Metadata = new CapsuleMetadata { TraceId = "t" },
            Trace = [new SpanRecord { SpanId = "dup", Name = "first" }, new SpanRecord { SpanId = "s2", Name = "second" }],
        };

        var merged = CapsuleMerger.Merge([a, b]);

        Assert.Equal(2, merged.Trace.Count);
    }

    [Fact]
    public void Merge_of_a_single_capsule_returns_it_unchanged()
    {
        var only = new Capsule { Metadata = new CapsuleMetadata { TraceId = "solo" } };
        Assert.Same(only, CapsuleMerger.Merge([only]));
    }
}
