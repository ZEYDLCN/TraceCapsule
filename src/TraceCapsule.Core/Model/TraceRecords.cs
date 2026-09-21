namespace TraceCapsule.Core.Model;

/// <summary>One span captured from the process's <see cref="System.Diagnostics.Activity"/>
/// graph (fed by ASP.NET Core / HttpClient / custom instrumentation via OpenTelemetry).</summary>
public sealed class SpanRecord
{
    public string SpanId { get; set; } = "";
    public string? ParentSpanId { get; set; }
    public string TraceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public double DurationMs { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
    public string Status { get; set; } = "Unset";
    public string? StatusDescription { get; set; }
}

/// <summary>An unhandled or recorded exception, tied to the span it occurred in.</summary>
public sealed class ExceptionRecord
{
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public string? StackTrace { get; set; }
    public string? SpanId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>Total wall-clock timing for the recorded execution.</summary>
public sealed class TimingRecord
{
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public double TotalDurationMs { get; set; }
}

/// <summary>A fingerprint of the configuration in effect when the capsule was recorded,
/// so a replay can detect "you're replaying against a different config" without ever
/// storing the actual (possibly sensitive) values.</summary>
public sealed class ConfigFingerprint
{
    public string Hash { get; set; } = "";
    public List<string> Keys { get; set; } = new();
}
