using System.Globalization;
using TraceCapsule.Core.Model;

namespace TraceCapsule.Core.Determinism;

/// <summary>Thrown when replay asks an <c>ITraceCapsule*</c> abstraction for one more value
/// than the capsule recorded — the replayed code took a different path than the one that was
/// recorded (e.g. an extra branch calls <c>Guid.NewGuid()</c> that didn't run in production).</summary>
public sealed class TraceCapsuleDeterminismException(string message) : Exception(message);

/// <summary>Hands back the exact values a capsule recorded, in the order it recorded them,
/// instead of generating new ones — this is what makes replay of code using
/// <see cref="ITraceCapsuleClock"/>/<see cref="ITraceCapsuleIdGenerator"/>/
/// <see cref="ITraceCapsuleRandom"/> deterministic. Register these in place of the
/// <c>Recording*</c> implementations when a host flips into replay mode, loading
/// <c>Capsule.Determinism</c> from the capsule being replayed.</summary>
public sealed class ReplayClock : ITraceCapsuleClock
{
    private readonly Queue<string> _values;

    public ReplayClock(IEnumerable<DeterminismEventRecord> events) =>
        _values = new Queue<string>(events.Where(e => e.Kind == DeterminismKind.Clock).OrderBy(e => e.Sequence).Select(e => e.Value));

    public DateTimeOffset UtcNow
    {
        get
        {
            if (_values.Count == 0)
            {
                throw new TraceCapsuleDeterminismException(
                    "Replay asked for a recorded clock value, but the capsule has none left — the replayed code path diverged from the recording.");
            }
            return DateTimeOffset.Parse(_values.Dequeue(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }
    }
}

public sealed class ReplayIdGenerator : ITraceCapsuleIdGenerator
{
    private readonly Queue<string> _values;

    public ReplayIdGenerator(IEnumerable<DeterminismEventRecord> events) =>
        _values = new Queue<string>(events.Where(e => e.Kind == DeterminismKind.Guid).OrderBy(e => e.Sequence).Select(e => e.Value));

    public Guid NewGuid()
    {
        if (_values.Count == 0)
        {
            throw new TraceCapsuleDeterminismException(
                "Replay asked for a recorded id, but the capsule has none left — the replayed code path diverged from the recording.");
        }
        return Guid.Parse(_values.Dequeue());
    }
}

public sealed class ReplayRandom : ITraceCapsuleRandom
{
    private readonly Queue<string> _values;

    public ReplayRandom(IEnumerable<DeterminismEventRecord> events) =>
        _values = new Queue<string>(events.Where(e => e.Kind == DeterminismKind.Random).OrderBy(e => e.Sequence).Select(e => e.Value));

    public int Next(int minValue, int maxValue) => int.Parse(Dequeue(), CultureInfo.InvariantCulture);

    public double NextDouble() => double.Parse(Dequeue(), CultureInfo.InvariantCulture);

    private string Dequeue()
    {
        if (_values.Count == 0)
        {
            throw new TraceCapsuleDeterminismException(
                "Replay asked for a recorded random value, but the capsule has none left — the replayed code path diverged from the recording.");
        }
        return _values.Dequeue();
    }
}
