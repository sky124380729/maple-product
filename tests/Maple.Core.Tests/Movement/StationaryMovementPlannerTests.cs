using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;

namespace Maple.Core.Tests.Movement;

public sealed class StationaryMovementPlannerTests
{
    [Fact]
    public void Facing_left_moves_right_then_left_with_independent_durations()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(123, 47, 87, 101));
        planner.StartSession(MovementDirection.Left);

        MovementPlan plan = planner.CreatePlan(TestConfig());

        Assert.Equal(MovementDirection.Right, plan.First.Direction);
        Assert.Equal(123, plan.First.HoldMs);
        Assert.Equal(MovementDirection.Left, plan.Second.Direction);
        Assert.Equal(87, plan.Second.HoldMs);
        Assert.Equal(47, plan.GapMs);
        Assert.Equal(101, plan.StabilizeMs);
        Assert.Equal(36, plan.ProjectedOffsetMs);
    }

    [Fact]
    public void Facing_right_moves_left_then_right()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(100, 30, 90, 80));
        planner.StartSession(MovementDirection.Right);

        MovementPlan plan = planner.CreatePlan(TestConfig());

        Assert.Equal(MovementDirection.Left, plan.First.Direction);
        Assert.Equal(MovementDirection.Right, plan.Second.Direction);
        Assert.Equal(-10, plan.ProjectedOffsetMs);
    }

    [Fact]
    public void Keeps_offset_across_cycles_and_resets_only_for_a_new_session()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(120, 30, 80, 80));
        planner.StartSession(MovementDirection.Right);
        MovementPlan plan = planner.CreatePlan(TestConfig());
        planner.ApplyCompletedPlan(plan);

        Assert.Equal(-40, planner.RelativeOffsetMs);

        planner.StartSession(MovementDirection.Left);
        Assert.Equal(0, planner.RelativeOffsetMs);
    }

    [Fact]
    public void Balanced_movement_can_continue_after_total_travel_exceeds_the_boundary()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(
            125, 30, 125, 80,
            125, 30, 125, 80,
            125, 30, 125, 80));
        planner.StartSession(MovementDirection.Right);
        StationaryAttackConfig config = TestConfig();

        MovementPlan first = planner.CreatePlan(config);
        planner.ApplyCompletedPlan(first);
        MovementPlan second = planner.CreatePlan(config);
        planner.ApplyCompletedPlan(second);
        MovementPlan third = planner.CreatePlan(config);
        planner.ApplyCompletedPlan(third);

        Assert.Equal(0, planner.RelativeOffsetMs);
        Assert.Equal(0, third.ProjectedOffsetMs);
    }

    [Fact]
    public void Does_not_swap_the_required_order_when_the_first_direction_has_no_budget()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource());
        planner.StartSession(MovementDirection.Right, relativeOffsetMs: -230);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => planner.CreatePlan(TestConfig()));

        Assert.Equal("INITIAL_FACING_BUDGET_EXHAUSTED", exception.Message);
    }

    [Fact]
    public void Stops_instead_of_shortening_the_second_hold_below_the_configured_minimum()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(80, 30, 30, 80));
        planner.StartSession(MovementDirection.Left, relativeOffsetMs: -300);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => planner.CreatePlan(TestConfig()));

        Assert.Equal("MOVEMENT_BUDGET_EXHAUSTED", exception.Message);
    }

    private sealed class SequenceRandomSource(params int[] values) : IRandomSource
    {
        private readonly Queue<int> values = new(values);

        public int NextInclusive(int minimum, int maximum)
        {
            int value = values.Dequeue();
            Assert.InRange(value, minimum, maximum);
            return value;
        }
    }

    private static StationaryAttackConfig TestConfig() => StationaryAttackConfig.Default with
    {
        MaxLateralMoveMs = 250,
        MoveHoldMinMs = 80,
        MoveHoldMaxMs = 125
    };
}
