namespace TraceCapsule.Core.Model;

public enum QueueEventDirection
{
    Publish,
    Consume,
}

/// <summary>A single message-queue event (RabbitMQ, Kafka, ...) captured for Phase 4
/// (message queue recording/replay).</summary>
public sealed class QueueEventRecord
{
    public string EventType { get; set; } = "";
    public string? MessageId { get; set; }
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public QueueEventDirection Direction { get; set; }
    public string ServiceName { get; set; } = "";
    public string Broker { get; set; } = "";
    public string Exchange { get; set; } = "";
    public string RoutingKey { get; set; } = "";
    public string Queue { get; set; } = "";
    public string? Payload { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public DateTimeOffset Timestamp { get; set; }
}
