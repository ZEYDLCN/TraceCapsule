using TraceCapsule.Core.Determinism;
using TraceCapsule.Core.Model;
using TraceCapsule.Core.Recording;

namespace TraceCapsule.UnitTests;

public class DeterminismTests
{
    [Fact]
    public void RecordingClock_appends_an_event_when_a_capsule_is_being_recorded()
    {
        using var scope = CapsuleRecordingContext.Begin("trace-1", null, out var recording);
        var clock = new RecordingClock();

        var value = clock.UtcNow;

        var evt = Assert.Single(recording.Determinism);
        Assert.Equal(DeterminismKind.Clock, evt.Kind);
        Assert.Equal(value, DateTimeOffset.Parse(evt.Value));
    }

    [Fact]
    public void RecordingIdGenerator_records_the_exact_guid_it_returned()
    {
        using var scope = CapsuleRecordingContext.Begin("trace-1", null, out var recording);
        var generator = new RecordingIdGenerator();

        var id = generator.NewGuid();

        var evt = Assert.Single(recording.Determinism);
        Assert.Equal(DeterminismKind.Guid, evt.Kind);
        Assert.Equal(id, Guid.Parse(evt.Value));
    }

    [Fact]
    public void Recording_implementations_do_nothing_extra_without_an_active_capsule()
    {
        var clock = new RecordingClock();
        var generator = new RecordingIdGenerator();
        var random = new RecordingRandom();

        // Just proving these don't throw and behave like the real thing with no ambient context.
        _ = clock.UtcNow;
        _ = generator.NewGuid();
        _ = random.NextDouble();
    }

    [Fact]
    public void Sequence_is_shared_across_clock_guid_and_random_in_call_order()
    {
        using var scope = CapsuleRecordingContext.Begin("trace-1", null, out var recording);
        var clock = new RecordingClock();
        var generator = new RecordingIdGenerator();

        _ = clock.UtcNow;    // sequence 0
        _ = generator.NewGuid(); // sequence 1
        _ = clock.UtcNow;    // sequence 2

        var ordered = recording.Determinism.OrderBy(e => e.Sequence).ToList();
        Assert.Equal([DeterminismKind.Clock, DeterminismKind.Guid, DeterminismKind.Clock], ordered.Select(e => e.Kind));
        Assert.Equal([0, 1, 2], ordered.Select(e => e.Sequence));
    }

    [Fact]
    public void Replay_reproduces_the_exact_recorded_values_in_order()
    {
        var recordedId1 = Guid.NewGuid();
        var recordedId2 = Guid.NewGuid();
        var events = new List<DeterminismEventRecord>
        {
            new() { Sequence = 0, Kind = DeterminismKind.Guid, Value = recordedId1.ToString() },
            new() { Sequence = 1, Kind = DeterminismKind.Guid, Value = recordedId2.ToString() },
        };
        var replay = new ReplayIdGenerator(events);

        Assert.Equal(recordedId1, replay.NewGuid());
        Assert.Equal(recordedId2, replay.NewGuid());
    }

    [Fact]
    public void ReplayClock_parses_the_recorded_timestamp_exactly()
    {
        var recorded = new DateTimeOffset(2026, 9, 21, 10, 14, 33, TimeSpan.Zero);
        var replay = new ReplayClock([new DeterminismEventRecord { Sequence = 0, Kind = DeterminismKind.Clock, Value = recorded.ToString("O") }]);

        Assert.Equal(recorded, replay.UtcNow);
    }

    [Fact]
    public void Replay_throws_a_clear_error_when_the_code_path_diverges_and_asks_for_more_values_than_recorded()
    {
        var replay = new ReplayIdGenerator([]);

        var ex = Assert.Throws<TraceCapsuleDeterminismException>(() => replay.NewGuid());
        Assert.Contains("diverged", ex.Message);
    }

    [Fact]
    public void ReplayRandom_reproduces_recorded_values_for_both_Next_and_NextDouble()
    {
        var events = new List<DeterminismEventRecord>
        {
            new() { Sequence = 0, Kind = DeterminismKind.Random, Value = "42" },
            new() { Sequence = 1, Kind = DeterminismKind.Random, Value = "0.5" },
        };
        var replay = new ReplayRandom(events);

        Assert.Equal(42, replay.Next(0, 100));
        Assert.Equal(0.5, replay.NextDouble());
    }

    [Fact]
    public async Task Record_round_trips_through_the_capsule_writer_and_reader()
    {
        using var scope = CapsuleRecordingContext.Begin("trace-1", null, out var recording);
        var generator = new RecordingIdGenerator();
        var id = generator.NewGuid();

        var capsule = new Capsule
        {
            Metadata = new Core.Model.CapsuleMetadata { TraceId = "trace-1" },
            Determinism = recording.Determinism.OrderBy(e => e.Sequence).ToList(),
        };

        using var buffer = new MemoryStream();
        await Core.Capsules.CapsuleWriter.WriteAsync(capsule, buffer);
        buffer.Position = 0;
        var loaded = await Core.Capsules.CapsuleReader.ReadAsync(buffer);

        var replay = new ReplayIdGenerator(loaded.Determinism);
        Assert.Equal(id, replay.NewGuid());
    }
}
