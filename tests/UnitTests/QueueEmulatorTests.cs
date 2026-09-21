using System.Diagnostics;
using TraceCapsule.Core.Fault;
using TraceCapsule.Core.Model;
using TraceCapsule.RabbitMQ;

namespace TraceCapsule.UnitTests;

public class QueueEmulatorTests
{
    private static QueueEventRecord Consume(string queue, string messageId, DateTimeOffset at) => new()
    {
        Direction = QueueEventDirection.Consume, Queue = queue, MessageId = messageId, Timestamp = at, EventType = "TransferRequested",
    };

    private static QueueEventRecord Publish(string messageId) => new()
    {
        Direction = QueueEventDirection.Publish, MessageId = messageId, EventType = "TransferRequested",
    };

    [Fact]
    public async Task Replays_consume_events_for_the_given_queue_in_recorded_order()
    {
        var t0 = DateTimeOffset.UtcNow;
        var emulator = new QueueEmulator([
            Consume("payments", "m2", t0.AddSeconds(1)),
            Consume("payments", "m1", t0),
            Consume("other-queue", "m3", t0),
        ]);

        var seen = new List<string?>();
        await emulator.ReplayAsync("payments", evt => { seen.Add(evt.MessageId); return Task.CompletedTask; });

        Assert.Equal(["m1", "m2"], seen);
    }

    [Fact]
    public void AllPublishedMessagesWereConsumed_is_true_when_every_publish_has_a_matching_consume()
    {
        var emulator = new QueueEmulator([Publish("m1"), Consume("q", "m1", DateTimeOffset.UtcNow)]);
        Assert.True(emulator.AllPublishedMessagesWereConsumed());
    }

    [Fact]
    public void AllPublishedMessagesWereConsumed_is_false_when_a_publish_has_no_matching_consume()
    {
        var emulator = new QueueEmulator([Publish("m1")]);
        Assert.False(emulator.AllPublishedMessagesWereConsumed());
    }

    [Fact]
    public async Task ReplayAsync_applies_injected_latency_for_the_named_queue()
    {
        var emulator = new QueueEmulator([Consume("payments", "m1", DateTimeOffset.UtcNow)]);
        using var faultScope = FaultInjectionContext.Begin(new FaultInjectionOptions().With("payments", new FaultSpec { ExtraLatencyMs = 120 }));

        var stopwatch = Stopwatch.StartNew();
        await emulator.ReplayAsync("payments", _ => Task.CompletedTask);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds >= 120, $"expected at least 120ms, was {stopwatch.ElapsedMilliseconds}ms");
    }
}
