namespace TraceCapsule.Core.Fault;

/// <summary>One injected fault for a named dependency, e.g. <c>--latency payment-api=3000</c>
/// on <c>tracecapsule replay</c> (Phase 6 / chaos replay).</summary>
public sealed class FaultSpec
{
    public int? ExtraLatencyMs { get; set; }
    public int? ForcedStatusCode { get; set; }
}

/// <summary>A set of faults to inject, keyed by dependency name, consulted by the replay-side
/// mock HTTP handler (<c>TraceCapsule.Http</c>) and the queue emulator
/// (<c>TraceCapsule.RabbitMQ</c>) so a recorded production scenario can be replayed with a
/// controlled failure layered on top — "Production Replay + Chaos Testing".</summary>
public sealed class FaultInjectionOptions
{
    public Dictionary<string, FaultSpec> Faults { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static FaultInjectionOptions None { get; } = new();

    public FaultInjectionOptions With(string dependencyName, FaultSpec spec)
    {
        Faults[dependencyName] = spec;
        return this;
    }

    /// <summary>Parses <c>name=valueMs</c> pairs as used by the CLI's <c>--latency</c> flag.</summary>
    public static FaultInjectionOptions ParseLatencyArgs(IEnumerable<string> latencyArgs)
    {
        var options = new FaultInjectionOptions();
        foreach (var arg in latencyArgs)
        {
            var parts = arg.Split('=', 2);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var ms)) continue;
            options.With(parts[0], new FaultSpec { ExtraLatencyMs = ms });
        }
        return options;
    }

    public bool TryGetFault(string dependencyName, out FaultSpec spec)
    {
        if (Faults.TryGetValue(dependencyName, out var found))
        {
            spec = found;
            return true;
        }
        spec = new FaultSpec();
        return false;
    }
}
