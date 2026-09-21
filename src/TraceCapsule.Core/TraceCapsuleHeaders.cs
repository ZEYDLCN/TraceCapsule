namespace TraceCapsule.Core;

/// <summary>Header names shared across every layer that needs to agree on them:
/// TraceCapsule.AspNetCore (reads/writes them), TraceCapsule.Http (forwards them onto
/// outbound calls so a distributed execution's session and fault instructions actually
/// propagate — see Phase 5/6), and TraceCapsule.Cli (sets them when replaying).</summary>
public static class TraceCapsuleHeaders
{
    /// <summary>Carries the distributed-execution session id (Phase 5) so each service's
    /// partial capsule can later be merged with <c>CapsuleMerger</c>.</summary>
    public const string Session = "X-TraceCapsule-Session";

    /// <summary>Carries Phase 6 fault-injection instructions, e.g.
    /// <c>"payment-api=3000"</c> — see <c>FaultInjectionOptions.ToHeaderValue</c>.</summary>
    public const string FaultLatency = "X-TraceCapsule-Fault-Latency";

    /// <summary>Forces capsule recording for this request regardless of the sampling
    /// policy.</summary>
    public const string ManualCapture = "X-TraceCapsule-Capture";
}
