using TraceCapsule.Core.Comparison;
using TraceCapsule.Core.Model;

namespace TraceCapsule.Cli.Compare;

public sealed record ComparisonLine(string Label, string ProductionValue, string ReplayValue, bool IsMatch);

public sealed record ComparisonReport(IReadOnlyList<ComparisonLine> Lines)
{
    public bool AllMatch => Lines.All(l => l.IsMatch);
}

/// <summary>Phase 2's <c>tracecapsule compare</c>: a small, explicit set of fields worth
/// diffing between a production capsule and a replay result — status code, exception
/// identity, per-span status for spans both sides recorded, and (Phase 8) a structural JSON
/// diff of the response body via <see cref="JsonBodyDiff"/>. Deliberately not a generic
/// deep-diff of everything: most fields (timestamps, exact durations, header ordering) are
/// *expected* to differ between two runs and would just be noise — <c>ignoreBodyPaths</c>
/// (`--ignore-body-field` on the CLI) is how the caller excludes the body's own volatile
/// fields (e.g. <c>"$.createdAt"</c>) from that diff.</summary>
public static class CapsuleComparer
{
    public static ComparisonReport Compare(Capsule production, Capsule replay, IEnumerable<string>? ignoreBodyPaths = null)
    {
        var lines = new List<ComparisonLine>
        {
            StatusLine(production, replay),
            ExceptionLine(production, replay),
        };
        lines.AddRange(SpanLines(production, replay));
        lines.AddRange(BodyLines(production, replay, ignoreBodyPaths));
        return new ComparisonReport(lines);
    }

    private static ComparisonLine StatusLine(Capsule production, Capsule replay)
    {
        var prodStatus = production.Response?.StatusCode.ToString() ?? "(no response)";
        var replayStatus = replay.Response?.StatusCode.ToString() ?? "(no response)";
        return new ComparisonLine("HTTP status", prodStatus, replayStatus, prodStatus == replayStatus);
    }

    private static ComparisonLine ExceptionLine(Capsule production, Capsule replay)
    {
        var prod = production.Exceptions.FirstOrDefault();
        var rep = replay.Exceptions.FirstOrDefault();
        var prodValue = prod is null ? "(none)" : prod.Type;
        var repValue = rep is null ? "(none)" : rep.Type;
        return new ComparisonLine("Exception", prodValue, repValue, prodValue == repValue);
    }

    private static IEnumerable<ComparisonLine> SpanLines(Capsule production, Capsule replay)
    {
        var replayByName = replay.Trace.ToLookup(s => s.Name);
        foreach (var span in production.Trace)
        {
            var match = replayByName[span.Name].FirstOrDefault();
            if (match is null) continue;
            yield return new ComparisonLine($"Span '{span.Name}'", span.Status, match.Status, span.Status == match.Status);
        }
    }

    private static IEnumerable<ComparisonLine> BodyLines(Capsule production, Capsule replay, IEnumerable<string>? ignoreBodyPaths)
    {
        var prodBody = production.Response?.Body;
        var replayBody = replay.Response?.Body;
        if (prodBody is null && replayBody is null) yield break;

        var differences = JsonBodyDiff.Compare(prodBody, replayBody, ignoreBodyPaths);
        if (differences.Count == 0)
        {
            yield return new ComparisonLine("Response body", "(matches, modulo ignored fields)", "(matches, modulo ignored fields)", true);
            yield break;
        }
        foreach (var diff in differences)
            yield return new ComparisonLine($"Body {diff.Path}", diff.Expected ?? "(missing)", diff.Actual ?? "(missing)", false);
    }
}
