namespace Maple.Core.Session;

public enum StationarySessionState
{
    Idle,
    LocatingWindow,
    ArmingBroker,
    Running,
    Stopped
}

public enum StationaryPhase
{
    Idle,
    AttackHolding,
    MoveFirst,
    MoveGap,
    MoveSecond,
    Stabilizing,
    Resting,
    Stopped
}

public sealed record StationaryRhythmState(
    int SchemaVersion,
    Guid SessionId,
    long CycleId,
    StationaryPhase Phase,
    int SampledDurationMs,
    long PhaseStartedMonoMs,
    long PhaseDeadlineMonoMs,
    int RemainingMs,
    long UpdatedAtMonoMs,
    string? EarlyReleaseReason);
