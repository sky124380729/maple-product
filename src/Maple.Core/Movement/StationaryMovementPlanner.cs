using Maple.Core.Configuration;
using Maple.Core.Rhythm;

namespace Maple.Core.Movement;

public enum MovementDirection
{
    Left = -1,
    Right = 1
}

public sealed record MovementSegment(MovementDirection Direction, int HoldMs);

public sealed record MovementPlan(
    MovementSegment First,
    int GapMs,
    MovementSegment Second,
    int StabilizeMs,
    int ProjectedOffsetMs);

public sealed class StationaryMovementPlanner(IRandomSource random)
{
    public int RelativeOffsetMs { get; private set; }
    public MovementDirection InitialFacing { get; private set; }

    public void StartSession(MovementDirection initialFacing, int relativeOffsetMs = 0)
    {
        InitialFacing = initialFacing;
        RelativeOffsetMs = relativeOffsetMs;
    }

    public MovementPlan CreatePlan(StationaryAttackConfig config)
    {
        MovementDirection firstDirection = InitialFacing == MovementDirection.Left
            ? MovementDirection.Right
            : MovementDirection.Left;
        int firstHold = SampleHold(
            firstDirection,
            RelativeOffsetMs,
            config,
            "INITIAL_FACING_BUDGET_EXHAUSTED");
        int afterFirst = Apply(RelativeOffsetMs, firstDirection, firstHold);
        int gap = random.NextInclusive(config.MoveGapMinMs, config.MoveGapMaxMs);
        MovementDirection secondDirection = InitialFacing;
        int secondHold = SampleHold(
            secondDirection,
            afterFirst,
            config,
            "MOVEMENT_BUDGET_EXHAUSTED");
        int projected = Apply(afterFirst, secondDirection, secondHold);
        int stabilize = random.NextInclusive(config.StabilizeMinMs, config.StabilizeMaxMs);
        return new MovementPlan(
            new MovementSegment(firstDirection, firstHold),
            gap,
            new MovementSegment(secondDirection, secondHold),
            stabilize,
            projected);
    }

    public void ApplyCompletedPlan(MovementPlan plan) => RelativeOffsetMs = plan.ProjectedOffsetMs;

    private int SampleHold(
        MovementDirection direction,
        int offset,
        StationaryAttackConfig config,
        string exhaustionCode)
    {
        int maximum = Math.Min(config.MoveHoldMaxMs, RemainingBudget(direction, offset, config.MaxLateralMoveMs));
        if (maximum < config.MoveHoldMinMs) throw new InvalidOperationException(exhaustionCode);
        return random.NextInclusive(config.MoveHoldMinMs, maximum);
    }

    private static int RemainingBudget(MovementDirection direction, int offset, int maximum) =>
        direction == MovementDirection.Left ? offset + maximum : maximum - offset;

    private static int Apply(int offset, MovementDirection direction, int holdMs) =>
        checked(offset + ((int)direction * holdMs));
}
