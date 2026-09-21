using TraceCapsule.Core.Model;
using TraceCapsule.Core.Recording;

namespace TraceCapsule.RabbitMQ;

/// <summary>Phase 4 recording side, kept deliberately free of any RabbitMQ.Client type so it
/// can be unit-tested without a broker. <see cref="RecordingChannelExtensions"/> and
/// <see cref="RecordingConsumerHandler"/> are the (thin, RabbitMQ.Client-specific) adapters
/// that extract these primitives from a real publish/delivery and call in here.</summary>
public static class QueueEventRecorder
{
    public static void RecordPublish(
        string serviceName, string exchange, string routingKey, string eventType,
        string? messageId, string? correlationId, byte[] body, IDictionary<string, string>? headers = null) =>
        Record(QueueEventDirection.Publish, serviceName, exchange, routingKey, "", eventType, messageId, correlationId, null, body, headers);

    public static void RecordConsume(
        string serviceName, string exchange, string routingKey, string queue, string eventType,
        string? messageId, string? correlationId, string? causationId, byte[] body, IDictionary<string, string>? headers = null) =>
        Record(QueueEventDirection.Consume, serviceName, exchange, routingKey, queue, eventType, messageId, correlationId, causationId, body, headers);

    private static void Record(
        QueueEventDirection direction, string serviceName, string exchange, string routingKey, string queue,
        string eventType, string? messageId, string? correlationId, string? causationId, byte[] body, IDictionary<string, string>? headers)
    {
        var recording = CapsuleRecordingContext.Current;
        if (recording is null) return;

        recording.Events.Add(new QueueEventRecord
        {
            EventType = eventType,
            MessageId = messageId,
            CorrelationId = correlationId,
            CausationId = causationId,
            Direction = direction,
            ServiceName = serviceName,
            Broker = "rabbitmq",
            Exchange = exchange,
            RoutingKey = routingKey,
            Queue = queue,
            Payload = BodyCapture.FromBytes(body, maxBytes: 64 * 1024),
            Headers = headers is null ? new Dictionary<string, string>() : new Dictionary<string, string>(headers),
            Timestamp = DateTimeOffset.UtcNow,
        });
    }
}
