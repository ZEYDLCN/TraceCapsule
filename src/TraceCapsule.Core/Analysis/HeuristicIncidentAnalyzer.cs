using TraceCapsule.Core.Model;

namespace TraceCapsule.Core.Analysis;

/// <summary>A dependency-free, rule-based <see cref="IIncidentAnalyzer"/>. It only ever
/// states what the capsule's own data shows (slowest span, failing external call, an
/// exception's span) — no external model call, no fabricated certainty.</summary>
public sealed class HeuristicIncidentAnalyzer : IIncidentAnalyzer
{
    public IncidentAnalysis Analyze(Capsule capsule)
    {
        var findings = new List<AnalysisFinding>();

        var exception = capsule.Exceptions.FirstOrDefault();
        if (exception is not null)
        {
            var span = capsule.Trace.FirstOrDefault(s => s.SpanId == exception.SpanId);
            var where = span is not null ? span.Name : "the request";
            findings.Add(new AnalysisFinding(
                "Likely root cause",
                $"{where} failed with {exception.Type}: {exception.Message}"));
        }

        var slowestSpan = capsule.Trace.OrderByDescending(s => s.DurationMs).FirstOrDefault();
        if (slowestSpan is not null && capsule.Trace.Count > 1)
        {
            findings.Add(new AnalysisFinding(
                "Slowest span",
                $"{slowestSpan.ServiceName}.{slowestSpan.Name} took {slowestSpan.DurationMs:F0}ms" +
                (capsule.Timing is { TotalDurationMs: > 0 } t ? $" ({slowestSpan.DurationMs / t.TotalDurationMs:P0} of total)" : "")));
        }

        var worstCall = capsule.ExternalHttpCalls
            .Where(call => call.ResponseStatusCode >= 500 || call.ResponseStatusCode == 0)
            .OrderByDescending(call => call.DurationMs)
            .FirstOrDefault();
        worstCall ??= capsule.ExternalHttpCalls.OrderByDescending(call => call.DurationMs).FirstOrDefault();
        if (worstCall is not null)
        {
            var status = worstCall.ResponseStatusCode == 0 ? "no response" : $"HTTP {worstCall.ResponseStatusCode}";
            findings.Add(new AnalysisFinding(
                "Possible contributing issue",
                $"External call to {worstCall.DependencyName} ({worstCall.Url}) took {worstCall.DurationMs:F0}ms and returned {status}."));
        }

        var unconsumed = capsule.Events
            .Where(e => e.Direction == QueueEventDirection.Publish)
            .Where(pub => pub.MessageId is not null &&
                          !capsule.Events.Any(e => e.Direction == QueueEventDirection.Consume && e.MessageId == pub.MessageId))
            .ToList();
        foreach (var pub in unconsumed)
        {
            findings.Add(new AnalysisFinding(
                "Message not consumed",
                $"'{pub.EventType}' (messageId {pub.MessageId}) was published to '{pub.RoutingKey}' but no matching consume event was recorded."));
        }

        return findings.Count == 0 ? IncidentAnalysis.Empty : new IncidentAnalysis(findings);
    }
}
