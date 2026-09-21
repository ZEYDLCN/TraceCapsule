using TraceCapsule.Core.Model;

namespace TraceCapsule.Core.Analysis;

/// <summary>Phase 7 (optional): analyzes a recorded capsule and suggests a likely root
/// cause. TraceCapsule's recording/replay/comparison pipeline never depends on this —
/// it is strictly an analysis layer that reads a <see cref="Capsule"/> after the fact.
/// A real deployment can swap <see cref="HeuristicIncidentAnalyzer"/> for an
/// LLM-backed implementation without touching anything else.</summary>
public interface IIncidentAnalyzer
{
    IncidentAnalysis Analyze(Capsule capsule);
}
