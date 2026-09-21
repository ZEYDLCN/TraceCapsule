using System.Collections.Concurrent;
using TraceCapsule.Core.Model;

namespace TraceCapsule.Core.Recording;

/// <summary>Ambient, per-request recording buffer. <c>TraceCapsuleMiddleware</c> creates
/// one of these when a request starts and flows it via <see cref="AsyncLocal{T}"/>, so
/// cross-cutting recorders that have no direct reference to the middleware — the outbound
/// HTTP handler (Phase 3), the RabbitMQ wrapper (Phase 4), the OpenTelemetry span listener —
/// can append to "the capsule currently being built" just by calling
/// <see cref="Current"/>. This mirrors how <see cref="System.Diagnostics.Activity.Current"/>
/// itself works, which is exactly what makes it composable across independently-registered
/// components.</summary>
public sealed class CapsuleRecordingContext
{
    private static readonly AsyncLocal<CapsuleRecordingContext?> Ambient = new();

    public static CapsuleRecordingContext? Current => Ambient.Value;

    public string TraceId { get; }
    public string? SessionId { get; }
    public ConcurrentBag<SpanRecord> Spans { get; } = [];
    public ConcurrentBag<ExternalHttpCallRecord> ExternalHttpCalls { get; } = [];
    public ConcurrentBag<QueueEventRecord> Events { get; } = [];
    public ConcurrentBag<ExceptionRecord> Exceptions { get; } = [];

    private CapsuleRecordingContext(string traceId, string? sessionId)
    {
        TraceId = traceId;
        SessionId = sessionId;
    }

    /// <summary>Starts a new ambient recording scope. Dispose the returned handle when the
    /// request completes to restore whatever context (if any) was active before — this
    /// matters when middleware is nested or requests are processed on a pooled thread.</summary>
    public static IDisposable Begin(string traceId, string? sessionId, out CapsuleRecordingContext context)
    {
        var previous = Ambient.Value;
        context = new CapsuleRecordingContext(traceId, sessionId);
        Ambient.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope(CapsuleRecordingContext? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
