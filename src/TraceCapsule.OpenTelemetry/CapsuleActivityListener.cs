using System.Diagnostics;
using TraceCapsule.Core.Model;
using TraceCapsule.Core.Recording;

namespace TraceCapsule.OpenTelemetry;

/// <summary>Bridges the .NET <see cref="Activity"/> graph (which ASP.NET Core's hosting
/// instrumentation, <c>HttpClient</c>, and any custom <see cref="ActivitySource"/> already
/// populate — the same graph OpenTelemetry exporters read from) into
/// <see cref="CapsuleRecordingContext"/>. Call <see cref="Enable"/> once at startup; every
/// activity that completes while a capsule recording context is active gets turned into a
/// <see cref="SpanRecord"/> automatically, with no per-call-site instrumentation needed.</summary>
public static class CapsuleActivityListener
{
    private static ActivityListener? _listener;
    private static int _enabled;

    /// <summary>Idempotent — safe to call multiple times (e.g. once per test), only the
    /// first call installs the listener.</summary>
    public static void Enable()
    {
        if (Interlocked.Exchange(ref _enabled, 1) == 1) return;

        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnActivityStopped,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Test-only: undoes <see cref="Enable"/> so listener state doesn't leak
    /// between test cases.</summary>
    public static void Disable()
    {
        _listener?.Dispose();
        _listener = null;
        Interlocked.Exchange(ref _enabled, 0);
    }

    private static void OnActivityStopped(Activity activity)
    {
        var recording = CapsuleRecordingContext.Current;
        if (recording is null) return;
        if (activity.TraceId.ToString() != recording.TraceId) return;

        recording.Spans.Add(new SpanRecord
        {
            SpanId = activity.SpanId.ToString(),
            ParentSpanId = activity.ParentSpanId == default ? null : activity.ParentSpanId.ToString(),
            TraceId = activity.TraceId.ToString(),
            Name = activity.DisplayName,
            ServiceName = activity.Source.Name,
            StartTime = activity.StartTimeUtc,
            EndTime = activity.StartTimeUtc + activity.Duration,
            DurationMs = activity.Duration.TotalMilliseconds,
            Attributes = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value?.ToString() ?? ""),
            Status = activity.Status.ToString(),
            StatusDescription = activity.StatusDescription,
        });
    }
}
