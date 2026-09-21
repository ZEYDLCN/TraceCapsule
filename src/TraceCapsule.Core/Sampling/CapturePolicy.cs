namespace TraceCapsule.Core.Sampling;

/// <summary>Decides whether a given execution is worth turning into a capsule. Mirrors the
/// README's sampling policy:
/// <code>
/// capture:
///   errors: true
///   latency_threshold_ms: 1500
///   sampling_rate: 0.001
/// </code>
/// Recording every request in production is wasteful; this makes "always capture errors,
/// rarely capture everything else" the default trade-off.</summary>
public sealed class CapturePolicy
{
    public bool CaptureErrors { get; set; } = true;
    public double LatencyThresholdMs { get; set; } = 1500;
    public double SamplingRate { get; set; } = 0.001;

    /// <summary>Manual triggers always win regardless of the policy below (explicit
    /// <c>--capture</c> / a specific trace id / an admin-initiated export).</summary>
    public bool ShouldCapture(bool isError, double durationMs, bool manualTrigger, Random? random = null)
    {
        if (manualTrigger) return true;
        if (isError && CaptureErrors) return true;
        if (durationMs >= LatencyThresholdMs) return true;
        random ??= Random.Shared;
        return random.NextDouble() < SamplingRate;
    }
}
