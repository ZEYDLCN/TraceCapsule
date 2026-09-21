namespace TraceCapsule.Core.Analysis;

/// <summary>One suggestion produced by an <see cref="IIncidentAnalyzer"/> — a likely root
/// cause or a possible contributing issue, never a certainty.</summary>
public sealed record AnalysisFinding(string Title, string Detail);

public sealed record IncidentAnalysis(IReadOnlyList<AnalysisFinding> Findings)
{
    public static readonly IncidentAnalysis Empty = new([]);
}
