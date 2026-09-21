using RabbitMQ.Client;

namespace TraceCapsule.RabbitMQ;

/// <summary>Thin RabbitMQ.Client adapter: publish exactly as you normally would, but also
/// record the event into the ambient capsule. This is intentionally a one-line wrapper
/// around <see cref="IChannel.BasicPublishAsync{TProperties}"/> — all the actual recording
/// logic lives in <see cref="QueueEventRecorder"/>, which has no RabbitMQ.Client dependency
/// and is what the unit tests exercise.</summary>
public static class RecordingChannelExtensions
{
    public static async ValueTask BasicPublishAndRecordAsync(
        this IChannel channel, string serviceName, string exchange, string routingKey, string eventType,
        BasicProperties properties, ReadOnlyMemory<byte> body, bool mandatory = false, CancellationToken cancellationToken = default)
    {
        await channel.BasicPublishAsync(exchange, routingKey, mandatory, properties, body, cancellationToken);
        QueueEventRecorder.RecordPublish(
            serviceName, exchange, routingKey, eventType,
            properties.MessageId, properties.CorrelationId, body.ToArray(), HeaderConversion.ToStringDictionary(properties.Headers));
    }
}

internal static class HeaderConversion
{
    public static Dictionary<string, string>? ToStringDictionary(IDictionary<string, object?>? headers) =>
        headers?.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
}
