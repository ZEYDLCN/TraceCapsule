using TraceCapsule.Core.Sampling;

namespace TraceCapsule.UnitTests;

public class CapturePolicyTests
{
    [Fact]
    public void Always_captures_errors_when_enabled()
    {
        var policy = new CapturePolicy { CaptureErrors = true, SamplingRate = 0 };
        Assert.True(policy.ShouldCapture(isError: true, durationMs: 5, manualTrigger: false, random: new Random(1)));
    }

    [Fact]
    public void Does_not_capture_errors_when_disabled_and_below_thresholds()
    {
        var policy = new CapturePolicy { CaptureErrors = false, LatencyThresholdMs = 10_000, SamplingRate = 0 };
        Assert.False(policy.ShouldCapture(isError: true, durationMs: 5, manualTrigger: false, random: new Random(1)));
    }

    [Fact]
    public void Captures_when_latency_threshold_is_exceeded()
    {
        var policy = new CapturePolicy { CaptureErrors = false, LatencyThresholdMs = 100, SamplingRate = 0 };
        Assert.True(policy.ShouldCapture(isError: false, durationMs: 150, manualTrigger: false, random: new Random(1)));
        Assert.False(policy.ShouldCapture(isError: false, durationMs: 50, manualTrigger: false, random: new Random(1)));
    }

    [Fact]
    public void Manual_trigger_always_wins()
    {
        var policy = new CapturePolicy { CaptureErrors = false, LatencyThresholdMs = 10_000, SamplingRate = 0 };
        Assert.True(policy.ShouldCapture(isError: false, durationMs: 1, manualTrigger: true));
    }

    [Fact]
    public void Sampling_rate_of_one_always_captures()
    {
        var policy = new CapturePolicy { CaptureErrors = false, LatencyThresholdMs = 10_000, SamplingRate = 1.0 };
        Assert.True(policy.ShouldCapture(isError: false, durationMs: 1, manualTrigger: false, random: new Random(42)));
    }

    [Fact]
    public void Sampling_rate_of_zero_never_captures_normal_requests()
    {
        var policy = new CapturePolicy { CaptureErrors = false, LatencyThresholdMs = 10_000, SamplingRate = 0.0 };
        for (var seed = 0; seed < 20; seed++)
        {
            Assert.False(policy.ShouldCapture(isError: false, durationMs: 1, manualTrigger: false, random: new Random(seed)));
        }
    }
}
