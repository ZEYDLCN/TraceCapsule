using TraceCapsule.Core.Model;
using TraceCapsule.Core.Recording;
using TraceCapsule.RabbitMQ;

namespace TraceCapsule.UnitTests;

public class QueueEventRecorderTests
{
    [Fact]
    public void RecordPublish_appends_a_publish_event_to_the_ambient_context()
    {
        using var scope = CapsuleRecordingContext.Begin("trace-1", null, out var recording);

        QueueEventRecorder.RecordPublish("transfer-service", "transfers", "transfer.requested", "TransferRequested", "m1", "corr-1", "{}"u8.ToArray());

        var evt = Assert.Single(recording.Events);
        Assert.Equal(QueueEventDirection.Publish, evt.Direction);
        Assert.Equal("m1", evt.MessageId);
        Assert.Equal("corr-1", evt.CorrelationId);
        Assert.Equal("transfer-service", evt.ServiceName);
    }

    [Fact]
    public void RecordConsume_appends_a_consume_event_with_the_queue_name()
    {
        using var scope = CapsuleRecordingContext.Begin("trace-1", null, out var recording);

        QueueEventRecorder.RecordConsume("payment-service", "transfers", "transfer.requested", "payments-queue", "TransferRequested", "m1", "corr-1", null, "{}"u8.ToArray());

        var evt = Assert.Single(recording.Events);
        Assert.Equal(QueueEventDirection.Consume, evt.Direction);
        Assert.Equal("payments-queue", evt.Queue);
    }

    [Fact]
    public void Does_nothing_when_no_capsule_is_being_recorded()
    {
        QueueEventRecorder.RecordPublish("svc", "ex", "rk", "Type", null, null, []);
        // no ambient context -> nothing to assert on except that it didn't throw
    }
}
