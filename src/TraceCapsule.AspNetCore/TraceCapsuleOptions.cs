using TraceCapsule.Core.Redaction;
using TraceCapsule.Core.Sampling;

namespace TraceCapsule.AspNetCore;

public sealed class TraceCapsuleOptions
{
    /// <summary>Records outbound <c>HttpClient</c> calls made through handlers registered
    /// with <c>AddTraceCapsuleRecording</c> (Phase 3). Middleware-level option so it can be
    /// toggled centrally even though the actual recording happens in TraceCapsule.Http.</summary>
    public bool EnableHttpRecording { get; set; } = true;

    /// <summary>Captures <see cref="System.Diagnostics.Activity"/> spans (via
    /// TraceCapsule.OpenTelemetry's listener) into the capsule's trace section.</summary>
    public bool EnableOpenTelemetry { get; set; } = true;

    public bool EnableRedaction { get; set; } = true;

    public RedactionPolicy RedactionPolicy { get; set; } = RedactionPolicy.Default();

    public CapturePolicy CapturePolicy { get; set; } = new();

    /// <summary>Where <c>.capsule</c> files are written. Relative paths are resolved
    /// against the current working directory.</summary>
    public string OutputDirectory { get; set; } = "capsules";

    /// <summary>Request header used to force-capture a specific request regardless of the
    /// sampling policy — <c>tracecapsule export --trace</c> tooling sets this.</summary>
    public string ManualCaptureHeaderName { get; set; } = Core.TraceCapsuleHeaders.ManualCapture;

    /// <summary>Request/response header that carries the distributed-execution session id
    /// so downstream services' partial capsules can be merged (Phase 5). Auto-generated for
    /// the entry-point service if absent on the inbound request, and forwarded onto outbound
    /// calls by <c>RecordingHttpMessageHandler</c> so it actually reaches the next hop.</summary>
    public string SessionHeaderName { get; set; } = Core.TraceCapsuleHeaders.Session;

    /// <summary>Request header carrying Phase 6 fault-injection instructions across process
    /// boundaries (CLI → target app, and service → service), e.g.
    /// <c>"payment-api=3000,fraud-api=500"</c>.
    /// See <see cref="Core.Fault.FaultInjectionOptions.ParseHeaderValue"/>.</summary>
    public string FaultHeaderName { get; set; } = Core.TraceCapsuleHeaders.FaultLatency;

    /// <summary>Caps how much of a request/response body is captured, to keep large
    /// payloads from blowing up capsule size or process memory. Bodies longer than this are
    /// truncated with a <c>"...(truncated)"</c> marker.</summary>
    public int MaxCapturedBodyBytes { get; set; } = 64 * 1024;

    public string AppVersion { get; set; } = "0.0.0";

    /// <summary>When <c>true</c>, only endpoints annotated with <c>[TraceCapsule]</c> are
    /// even considered for recording — everything else bypasses the middleware entirely.
    /// Default <c>false</c>: every request goes through the capture policy.</summary>
    public bool RequireAttribute { get; set; }
}
