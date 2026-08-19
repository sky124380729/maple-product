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

    public void StartSession(int relativeOffsetMs = 0) => RelativeOffsetMs = relativeOffsetMs;

    public MovementPlan CreatePlan(StationaryAttackConfig config)
    {
        bool canLeft = RemainingBudget(MovementDirection.Left, RelativeOffsetMs, config.MaxLateralMoveMs) >= config.MoveHoldMinMs;
        bool canRight = RemainingBudget(MovementDirection.Right, RelativeOffsetMs, config.MaxLateralMoveMs) >= config.MoveHoldMinMs;
        if (!canLeft && !canRight) throw new InvalidOperationException("No safe first movement direction is available.");

        MovementDirection firstDirection = canLeft && canRight
            ? (random.NextInclusive(0, 1) == 0 ? MovementDirection.Left : MovementDirection.Right)
            : canLeft ? MovementDirection.Left : MovementDirection.Right;
        int firstHold = SampleHold(firstDirection, RelativeOffsetMs, config);
        int afterFirst = Apply(RelativeOffsetMs, firstDirection, firstHold);
        int gap = random.NextInclusive(config.MoveGapMinMs, config.MoveGapMaxMs);
        MovementDirection secondDirection = firstDirection == MovementDirection.Left
            ? MovementDirection.Right
            : MovementDirection.Left;
        int secondHold = SampleHold(secondDirection, afterFirst, config);
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

    private int SampleHold(MovementDirection direction, int offset, StationaryAttackConfig config)
    {
        int maximum = Math.Min(config.MoveHoldMaxMs, RemainingBudget(direction, offset, config.MaxLateralMoveMs));
        if (maximum <= 0) throw new InvalidOperationException("No safe movement budget is available.");
        int minimum = Math.Min(config.MoveHoldMinMs, maximum);
        return random.NextInclusive(minimum, maximum);
    }

    private static int RemainingBudget(MovementDirection direction, int offset, int maximum) =>
        direction == MovementDirection.Left ? offset + maximum : maximum - offset;

    private static int Apply(int offset, MovementDirection direction, int holdMs) =>
        checked(offset + ((int)direction * holdMs));
}
