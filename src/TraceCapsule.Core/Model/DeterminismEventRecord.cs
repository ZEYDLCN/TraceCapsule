namespace TraceCapsule.Core.Model;

public enum DeterminismKind
{
    Clock,
    Guid,
    Random,
}

/// <summary>One non-deterministic value the application asked TraceCapsule for during
/// recording — <c>DateTime.UtcNow</c>, <c>Guid.NewGuid()</c>, <c>Random.Next()</c> — captured
/// so replay can hand back the exact same value instead of a new one. See the README's
/// "Deterministic Replay Problem": production got <c>12:00:01</c> / <c>abc-123</c>, replay at
/// <c>14:32:52</c> / <c>xyz-991</c> can send the application down a different code path
/// entirely. <see cref="Sequence"/> is a single counter shared across all three kinds (not
/// per-kind), so replay reproduces the exact chronological order calls happened in.</summary>
public sealed class DeterminismEventRecord
{
    public int Sequence { get; set; }
    public DeterminismKind Kind { get; set; }
    public string Value { get; set; } = "";
}
