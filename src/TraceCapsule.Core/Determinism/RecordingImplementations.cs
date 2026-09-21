using System.Globalization;
using TraceCapsule.Core.Model;
using TraceCapsule.Core.Recording;

namespace TraceCapsule.Core.Determinism;

internal static class DeterminismRecorder
{
    public static void Record(DeterminismKind kind, string value)
    {
        var recording = CapsuleRecordingContext.Current;
        if (recording is null) return;
        recording.Determinism.Add(new DeterminismEventRecord
        {
            Sequence = recording.NextDeterminismSequence(),
            Kind = kind,
            Value = value,
        });
    }
}

/// <summary>The default, production implementations: return a real value exactly like the
/// BCL would, and — if a capsule is currently being recorded — also append it to
/// <see cref="CapsuleRecordingContext.Determinism"/> so replay can reproduce it later. A
/// no-op when there's no active recording (e.g. a background job, or sampling decided this
/// request isn't worth capturing).</summary>
public sealed class RecordingClock : ITraceCapsuleClock
{
    public DateTimeOffset UtcNow
    {
        get
        {
            var value = DateTimeOffset.UtcNow;
            DeterminismRecorder.Record(DeterminismKind.Clock, value.ToString("O", CultureInfo.InvariantCulture));
            return value;
        }
    }
}

public sealed class RecordingIdGenerator : ITraceCapsuleIdGenerator
{
    public Guid NewGuid()
    {
        var value = Guid.NewGuid();
        DeterminismRecorder.Record(DeterminismKind.Guid, value.ToString());
        return value;
    }
}

public sealed class RecordingRandom : ITraceCapsuleRandom
{
    public int Next(int minValue, int maxValue)
    {
        var value = Random.Shared.Next(minValue, maxValue);
        DeterminismRecorder.Record(DeterminismKind.Random, value.ToString(CultureInfo.InvariantCulture));
        return value;
    }

    public double NextDouble()
    {
        var value = Random.Shared.NextDouble();
        DeterminismRecorder.Record(DeterminismKind.Random, value.ToString("R", CultureInfo.InvariantCulture));
        return value;
    }
}
