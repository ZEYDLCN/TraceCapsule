using TraceCapsule.Core.Fault;
using TraceCapsule.Core.Model;

namespace TraceCapsule.RabbitMQ;

/// <summary>The "Queue Emulator" from the README's Replay Architecture diagram: replays a
/// capsule's recorded consume events, in their original order, into a handler — without a
/// real RabbitMQ (or any) broker running. Consulting <see cref="FaultInjectionContext"/>
/// lets a replay inject extra latency on a named queue for Phase 6 chaos testing.</summary>
public sealed class QueueEmulator(IEnumerable<QueueEventRecord> recordedEvents)
{
    private readonly List<QueueEventRecord> _events = recordedEvents.OrderBy(e => e.Timestamp).ToList();

    public IReadOnlyList<QueueEventRecord> RecordedEvents => _events;

    /// <summary>Replays every recorded <see cref="QueueEventDirection.Consume"/> event for
    /// <paramref name="queue"/>, in recording order, awaiting <paramref name="handler"/>
    /// between each one so consumer side effects happen in the same sequence they did in
    /// production.</summary>
    public async Task ReplayAsync(string queue, Func<QueueEventRecord, Task> handler, CancellationToken cancellationToken = default)
    {
        var fault = FaultInjectionContext.Current;
        foreach (var evt in _events.Where(e => e.Direction == QueueEventDirection.Consume && e.Queue == queue))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fault.TryGetFault(queue, out var spec) && spec.ExtraLatencyMs is > 0)
            {
                await Task.Delay(spec.ExtraLatencyMs.Value, cancellationToken);
            }
            await handler(evt);
        }
    }

    /// <summary>True if every publish in the capsule has a matching consume with the same
    /// message id — a quick, capsule-only sanity check before a full replay
    /// (complements <c>HeuristicIncidentAnalyzer</c>'s "message not consumed" finding).</summary>
    public bool AllPublishedMessagesWereConsumed() =>
        _events.Where(e => e.Direction == QueueEventDirection.Publish && e.MessageId is not null)
            .All(pub => _events.Any(e => e.Direction == QueueEventDirection.Consume && e.MessageId == pub.MessageId));
}
