namespace TraceCapsule.Core.Model;

public sealed class RequestInfo
{
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class EnvironmentInfo
{
    public string AppVersion { get; set; } = "";
    public ConfigFingerprint? ConfigFingerprint { get; set; }
}

public sealed class CapsuleMetadata
{
    public string TraceId { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public RequestInfo Request { get; set; } = new();
    public List<string> Services { get; set; } = new();
    public EnvironmentInfo Environment { get; set; } = new();

    /// <summary>Set when this capsule is one service's partial contribution to a larger,
    /// multi-service execution (Phase 5). Capsules that share the same SessionId can be
    /// combined with <c>CapsuleMerger</c>.</summary>
    public string? SessionId { get; set; }
}

/// <summary>The full, in-memory representation of a <c>.capsule</c> file: everything
/// TraceCapsule recorded about one execution. <see cref="Capsules.CapsuleWriter"/> and
/// <see cref="Capsules.CapsuleReader"/> convert this to/from the on-disk ZIP layout
/// described in the README (metadata.json, request.json, trace.json, ...).</summary>
public sealed class Capsule
{
    public CapsuleMetadata Metadata { get; set; } = new();
    public HttpRequestRecord? Request { get; set; }
    public HttpResponseRecord? Response { get; set; }
    public List<SpanRecord> Trace { get; set; } = new();
    public List<ExternalHttpCallRecord> ExternalHttpCalls { get; set; } = new();
    public List<QueueEventRecord> Events { get; set; } = new();
    public List<ExceptionRecord> Exceptions { get; set; } = new();
    public TimingRecord? Timing { get; set; }
}
