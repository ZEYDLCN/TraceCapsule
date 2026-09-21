using RabbitMQ.Client.Events;

namespace TraceCapsule.RabbitMQ;

/// <summary>Wraps a consumer's own message handler so every delivery is recorded before the
/// handler runs. Register on an <see cref="AsyncEventingBasicConsumer"/>:
/// <code>
/// consumer.ReceivedAsync += RecordingConsumerHandler.Wrap(
///     "payment-service", "payments-queue", "TransferRequested",
///     async args => { /* your handler */ });
/// </code>
/// </summary>
public static class RecordingConsumerHandler
{
    public static AsyncEventHandler<BasicDeliverEventArgs> Wrap(
        string serviceName, string queue, string eventType, Func<BasicDeliverEventArgs, Task> handler) =>
        (_, args) =>
        {
            QueueEventRecorder.RecordConsume(
                serviceName, args.Exchange, args.RoutingKey, queue, eventType,
                args.BasicProperties.MessageId, args.BasicProperties.CorrelationId, causationId: null,
                body: args.Body.ToArray(), headers: HeaderConversion.ToStringDictionary(args.BasicProperties.Headers));
            return handler(args);
        };
}
