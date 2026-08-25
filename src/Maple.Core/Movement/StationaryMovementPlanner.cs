using Maple.Core.Configuration;
using Maple.Core.Rhythm;

namespace Maple.Core.Movement;

public enum MovementDirection
{
    Left = -1,
    Right = 1
}

public enum MovementIntent
{
    Unbiased,
    ReturnTowardCenter,
    RecoveryTowardCenter
}

public sealed record MovementCycle(int StartOffsetMs, MovementIntent Intent);

public sealed record MovementSegment(MovementDirection Direction, int HoldMs);

public sealed class StationaryMovementPlanner(IRandomSource random)
{
    public const int ReleaseSafetyMarginMs = 20;

    private bool returnTowardCenterRequired;

    public int RelativeOffsetMs { get; private set; }
    public MovementDirection InitialFacing { get; private set; }

    public void StartSession(MovementDirection initialFacing, int relativeOffsetMs = 0)
    {
        InitialFacing = initialFacing;
        RelativeOffsetMs = relativeOffsetMs;
        returnTowardCenterRequired = false;
    }

    public MovementCycle BeginCycle(StationaryAttackConfig config)
    {
        ValidateCurrentOffset(config.MaxLateralMoveMs);

        MovementIntent intent;
        if (returnTowardCenterRequired)
        {
            intent = MovementIntent.ReturnTowardCenter;
        }
        else
        {
            long scaledOffset = Math.Abs((long)RelativeOffsetMs) * 100;
            long scaledMaximum = (long)config.MaxLateralMoveMs;
            intent = scaledOffset <= scaledMaximum * 40
                ? MovementIntent.Unbiased
                : scaledOffset <= scaledMaximum * 70
                    ? random.NextInclusive(1, 100) <= 75
                        ? MovementIntent.ReturnTowardCenter
                        : MovementIntent.Unbiased
                    : MovementIntent.ReturnTowardCenter;
        }
        return new MovementCycle(RelativeOffsetMs, intent);
    }

    public void ValidateCurrentOffset(int maximumOffsetMs)
    {
        if (Math.Abs((long)RelativeOffsetMs) > maximumOffsetMs)
            throw new InvalidOperationException("MOVEMENT_OFFSET_EXCEEDED");
    }

    public MovementSegment CreateFirstSegment(StationaryAttackConfig config, MovementCycle cycle)
    {
        return TryCreateFirstSegment(config, cycle) ??
            throw new InvalidOperationException("INITIAL_FACING_BUDGET_EXHAUSTED");
    }

    public MovementSegment? TryCreateFirstSegment(StationaryAttackConfig config, MovementCycle cycle)
    {
        MovementDirection direction = FirstDirection();
        IReadOnlyList<int> candidates = SafeHolds(direction, RelativeOffsetMs, config)
            .Where(hold => LeavesLegalSecondForAllowedActualRange(config, cycle, direction, hold))
            .ToArray();
        return TryCreateSegment(direction, candidates);
    }

    public MovementSegment? TryCreateRecoverySegment(StationaryAttackConfig config)
    {
        IReadOnlyList<int> candidates = SafeHolds(InitialFacing, RelativeOffsetMs, config).ToArray();
        return TryCreateSegment(InitialFacing, candidates);
    }

    public MovementSegment CreateSecondSegment(StationaryAttackConfig config, MovementCycle cycle)
    {
        IReadOnlyList<int> candidates = LegalSecondHolds(config, cycle, RelativeOffsetMs);
        return new MovementSegment(InitialFacing, Pick(candidates, "MOVEMENT_BUDGET_EXHAUSTED"));
    }

    public void ApplyCompletedSegment(
        MovementDirection direction,
        int actualHoldMs,
        int maximumOffsetMs)
    {
        long updated = (long)RelativeOffsetMs + ((int)direction * (long)actualHoldMs);
        if (Math.Abs(updated) > maximumOffsetMs)
            throw new InvalidOperationException("MOVEMENT_OFFSET_EXCEEDED");
        RelativeOffsetMs = checked((int)updated);
    }

    public void CompleteCycle(StationaryAttackConfig config, MovementCycle cycle)
    {
        returnTowardCenterRequired = cycle.Intent == MovementIntent.ReturnTowardCenter &&
            Math.Abs((long)RelativeOffsetMs) > Math.Abs((long)cycle.StartOffsetMs);

    }

    public int SampleGapMs(StationaryAttackConfig config) =>
        random.NextInclusive(config.MoveGapMinMs, config.MoveGapMaxMs);

    public int SampleStabilizeMs(StationaryAttackConfig config) =>
        random.NextInclusive(config.StabilizeMinMs, config.StabilizeMaxMs);

    private bool LeavesLegalSecondForAllowedActualRange(
        StationaryAttackConfig config,
        MovementCycle cycle,
        MovementDirection direction,
        int plannedHoldMs)
    {
        int maximumActualHoldMs = checked(plannedHoldMs + ReleaseSafetyMarginMs);
        for (int actualHoldMs = plannedHoldMs; actualHoldMs <= maximumActualHoldMs; actualHoldMs++)
        {
            int projected = Apply(RelativeOffsetMs, direction, actualHoldMs);
            if (LegalSecondHolds(config, cycle, projected).Count == 0) return false;
        }
        return true;
    }

    private IReadOnlyList<int> LegalSecondHolds(
        StationaryAttackConfig config,
        MovementCycle cycle,
        int offset)
    {
        MovementDirection nextFirst = FirstDirection();
        long requiredNextFirstBudget = config.MoveHoldMinMs + ReleaseSafetyMarginMs;
        return SafeHolds(InitialFacing, offset, config)
            .Where(hold =>
            {
                int projected = Apply(offset, InitialFacing, hold);
                if (RemainingBudget(nextFirst, projected, config.MaxLateralMoveMs) < requiredNextFirstBudget)
                    return false;
                return cycle.Intent != MovementIntent.ReturnTowardCenter ||
                    Math.Abs((long)projected) <= Math.Abs((long)cycle.StartOffsetMs);
            })
            .ToArray();
    }

    private IEnumerable<int> SafeHolds(
        MovementDirection direction,
        int offset,
        StationaryAttackConfig config)
    {
        int maximum = checked((int)Math.Min(
            (long)config.MoveHoldMaxMs,
            RemainingBudget(direction, offset, config.MaxLateralMoveMs) - ReleaseSafetyMarginMs));
        for (int hold = config.MoveHoldMinMs; hold <= maximum; hold++)
            yield return hold;
    }

    private int Pick(IReadOnlyList<int> candidates, string exhaustionCode)
    {
        if (candidates.Count == 0) throw new InvalidOperationException(exhaustionCode);
        return candidates[random.NextInclusive(0, candidates.Count - 1)];
    }

    private MovementSegment? TryCreateSegment(
        MovementDirection direction,
        IReadOnlyList<int> candidates) =>
        candidates.Count == 0
            ? null
            : new MovementSegment(direction, candidates[random.NextInclusive(0, candidates.Count - 1)]);

    private MovementDirection FirstDirection() =>
        InitialFacing == MovementDirection.Left ? MovementDirection.Right : MovementDirection.Left;

    private static long RemainingBudget(MovementDirection direction, int offset, int maximum) =>
        direction == MovementDirection.Left ? (long)offset + maximum : (long)maximum - offset;

    private static int Apply(int offset, MovementDirection direction, int holdMs) =>
        checked(offset + ((int)direction * holdMs));
}
