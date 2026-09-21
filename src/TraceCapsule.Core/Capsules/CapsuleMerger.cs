using TraceCapsule.Core.Model;

namespace TraceCapsule.Core.Capsules;

/// <summary>Phase 5 (distributed replay): each service in a multi-service execution records
/// its own partial capsule (they share a <see cref="CapsuleMetadata.SessionId"/> propagated
/// via a trace header — see <c>TraceCapsuleOptions.SessionHeaderName</c>). Once every
/// participating service has flushed its partial capsule, <see cref="Merge"/> combines them
/// into the single, complete artifact a developer actually wants to inspect/replay.</summary>
public static class CapsuleMerger
{
    public static Capsule Merge(IReadOnlyCollection<Capsule> parts)
    {
        if (parts.Count == 0) throw new ArgumentException("At least one capsule is required to merge.", nameof(parts));
        if (parts.Count == 1) return parts.Single();

        var traceIds = parts.Select(p => p.Metadata.TraceId).Distinct().ToList();
        if (traceIds.Count > 1)
        {
            throw new InvalidOperationException(
                $"Cannot merge capsules with different trace ids: {string.Join(", ", traceIds)}. " +
                "Merge only combines partial capsules from the same distributed execution.");
        }

        var ordered = parts.OrderBy(p => p.Metadata.Timestamp).ToList();
        var entryPoint = ordered.FirstOrDefault(p => p.Request is not null) ?? ordered[0];

        var merged = new Capsule
        {
            Metadata = new CapsuleMetadata
            {
                TraceId = entryPoint.Metadata.TraceId,
                Timestamp = ordered.Min(p => p.Metadata.Timestamp),
                Request = entryPoint.Metadata.Request,
                SessionId = entryPoint.Metadata.SessionId,
                Services = ordered.SelectMany(p => p.Metadata.Services).Distinct().ToList(),
                Environment = entryPoint.Metadata.Environment,
            },
            Request = entryPoint.Request,
            Response = ordered.LastOrDefault(p => p.Response is not null)?.Response,
            Trace = ordered.SelectMany(p => p.Trace).GroupBy(s => s.SpanId).Select(g => g.First()).ToList(),
            ExternalHttpCalls = ordered.SelectMany(p => p.ExternalHttpCalls).ToList(),
            Events = ordered.SelectMany(p => p.Events).OrderBy(e => e.Timestamp).ToList(),
            Exceptions = ordered.SelectMany(p => p.Exceptions).ToList(),
        };

        var timings = ordered.Where(p => p.Timing is not null).Select(p => p.Timing!).ToList();
        if (timings.Count > 0)
        {
            var start = timings.Min(t => t.StartedAt);
            var end = timings.Max(t => t.CompletedAt);
            merged.Timing = new TimingRecord
            {
                StartedAt = start,
                CompletedAt = end,
                TotalDurationMs = (end - start).TotalMilliseconds,
            };
        }

        return merged;
    }
}
