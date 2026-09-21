using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TraceCapsule.Core.Recording;
using TraceCapsule.RabbitMQ;

namespace TraceCapsule.UnitTests;

public class RecordingConsumerHandlerTests
{
    [Fact]
    public async Task Records_the_delivery_then_invokes_the_inner_handler()
    {
        using var scope = CapsuleRecordingContext.Begin("trace-1", null, out var recording);

        var properties = new BasicProperties { MessageId = "m1", CorrelationId = "corr-1" };
        var args = new BasicDeliverEventArgs(
            consumerTag: "ct", deliveryTag: 1, redelivered: false, exchange: "transfers", routingKey: "transfer.requested",
            properties: properties, body: "{\"amount\":5000}"u8.ToArray(), cancellationToken: CancellationToken.None);

        var innerHandlerCalled = false;
        var wrapped = RecordingConsumerHandler.Wrap("payment-service", "payments-queue", "TransferRequested", _ =>
        {
            innerHandlerCalled = true;
            return Task.CompletedTask;
        });

        await wrapped(this, args);

        Assert.True(innerHandlerCalled);
        var evt = Assert.Single(recording.Events);
        Assert.Equal("m1", evt.MessageId);
        Assert.Equal("payments-queue", evt.Queue);
        Assert.Equal("payment-service", evt.ServiceName);
    }
}
