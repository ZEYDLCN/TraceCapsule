namespace TraceCapsule.Core.Determinism;

/// <summary>Everywhere the README's "Deterministic Replay Problem" applies: code that calls
/// <c>DateTime.UtcNow</c> directly can't be replayed deterministically, because there's no
/// hook to intercept a static BCL call. An application has to code against these
/// abstractions instead — exactly the trade-off the README itself describes
/// (<c>ISystemClock</c>) — in exchange for TraceCapsule being able to record what value it
/// got and hand back the identical one on replay.</summary>
public interface ITraceCapsuleClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ITraceCapsuleIdGenerator
{
    Guid NewGuid();
}

public interface ITraceCapsuleRandom
{
    int Next(int minValue, int maxValue);
    double NextDouble();
}
