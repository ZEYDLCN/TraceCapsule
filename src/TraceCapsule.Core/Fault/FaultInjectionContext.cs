namespace TraceCapsule.Core.Fault;

/// <summary>Ambient, per-request fault injection settings (Phase 6). Mirrors
/// <c>CapsuleRecordingContext</c>: a middleware/entry point sets this once at the top of a
/// request (from a CLI-supplied header, a config flag, or a test harness), and anything
/// downstream that can inject a fault — <c>ReplayHttpMessageHandler</c>, the queue emulator —
/// consults <see cref="Current"/> without needing it threaded through every call.</summary>
public static class FaultInjectionContext
{
    private static readonly AsyncLocal<FaultInjectionOptions?> Ambient = new();

    public static FaultInjectionOptions Current => Ambient.Value ?? FaultInjectionOptions.None;

    public static IDisposable Begin(FaultInjectionOptions options)
    {
        var previous = Ambient.Value;
        Ambient.Value = options;
        return new Scope(previous);
    }

    private sealed class Scope(FaultInjectionOptions? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
