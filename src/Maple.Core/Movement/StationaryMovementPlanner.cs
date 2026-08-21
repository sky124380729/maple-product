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
    public int LeftTravelMs { get; private set; }
    public int RightTravelMs { get; private set; }
    public MovementDirection InitialFacing { get; private set; }

    public void StartSession(MovementDirection initialFacing, int relativeOffsetMs = 0)
    {
        InitialFacing = initialFacing;
        RelativeOffsetMs = relativeOffsetMs;
        LeftTravelMs = Math.Max(0, -relativeOffsetMs);
        RightTravelMs = Math.Max(0, relativeOffsetMs);
    }

    public MovementPlan CreatePlan(StationaryAttackConfig config)
    {
        MovementDirection firstDirection = InitialFacing == MovementDirection.Left
            ? MovementDirection.Right
            : MovementDirection.Left;
        int firstHold = SampleHold(firstDirection, LeftTravelMs, RightTravelMs, config, "INITIAL_FACING_BUDGET_EXHAUSTED");
        int afterFirst = Apply(RelativeOffsetMs, firstDirection, firstHold);
        int leftAfterFirst = LeftTravelMs + (firstDirection == MovementDirection.Left ? firstHold : 0);
        int rightAfterFirst = RightTravelMs + (firstDirection == MovementDirection.Right ? firstHold : 0);
        int gap = random.NextInclusive(config.MoveGapMinMs, config.MoveGapMaxMs);
        MovementDirection secondDirection = InitialFacing;
        int secondHold = SampleHold(secondDirection, leftAfterFirst, rightAfterFirst, config, "MOVEMENT_BUDGET_EXHAUSTED");
        int projected = Apply(afterFirst, secondDirection, secondHold);
        int stabilize = random.NextInclusive(config.StabilizeMinMs, config.StabilizeMaxMs);
        return new MovementPlan(
            new MovementSegment(firstDirection, firstHold),
            gap,
            new MovementSegment(secondDirection, secondHold),
            stabilize,
            projected);
    }

    public void ApplyCompletedPlan(MovementPlan plan)
    {
        LeftTravelMs += plan.First.Direction == MovementDirection.Left ? plan.First.HoldMs : 0;
        LeftTravelMs += plan.Second.Direction == MovementDirection.Left ? plan.Second.HoldMs : 0;
        RightTravelMs += plan.First.Direction == MovementDirection.Right ? plan.First.HoldMs : 0;
        RightTravelMs += plan.Second.Direction == MovementDirection.Right ? plan.Second.HoldMs : 0;
        RelativeOffsetMs = plan.ProjectedOffsetMs;
    }

    private int SampleHold(
        MovementDirection direction,
        int leftTravelMs,
        int rightTravelMs,
        StationaryAttackConfig config,
        string exhaustionCode)
    {
        int consumed = direction == MovementDirection.Left ? leftTravelMs : rightTravelMs;
        int maximum = Math.Min(config.MoveHoldMaxMs, config.MaxLateralMoveMs - consumed);
        if (maximum < config.MoveHoldMinMs) throw new InvalidOperationException(exhaustionCode);
        return random.NextInclusive(config.MoveHoldMinMs, maximum);
    }

    private static int Apply(int offset, MovementDirection direction, int holdMs) =>
        checked(offset + ((int)direction * holdMs));
}
