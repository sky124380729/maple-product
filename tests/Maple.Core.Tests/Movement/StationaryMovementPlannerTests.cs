using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;

namespace Maple.Core.Tests.Movement;

public sealed class StationaryMovementPlannerTests
{
    [Fact]
    public void Produces_opposite_directions_with_independent_durations()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(0, 123, 47, 87, 101));
        planner.StartSession();

        MovementPlan plan = planner.CreatePlan(StationaryAttackConfig.Default);

        Assert.Equal(MovementDirection.Left, plan.First.Direction);
        Assert.Equal(123, plan.First.HoldMs);
        Assert.Equal(MovementDirection.Right, plan.Second.Direction);
        Assert.Equal(87, plan.Second.HoldMs);
        Assert.Equal(47, plan.GapMs);
        Assert.Equal(101, plan.StabilizeMs);
        Assert.Equal(-36, plan.ProjectedOffsetMs);
    }

    [Fact]
    public void Keeps_offset_across_cycles_and_resets_only_for_a_new_session()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(0, 120, 30, 80, 80));
        planner.StartSession();
        MovementPlan plan = planner.CreatePlan(StationaryAttackConfig.Default);
        planner.ApplyCompletedPlan(plan);

        Assert.Equal(-40, planner.RelativeOffsetMs);

        planner.StartSession();
        Assert.Equal(0, planner.RelativeOffsetMs);
    }

    [Fact]
    public void Never_selects_a_direction_without_minimum_remaining_budget()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(100, 30, 80, 80));
        planner.StartSession(relativeOffsetMs: 230);

        MovementPlan plan = planner.CreatePlan(StationaryAttackConfig.Default);

        Assert.Equal(MovementDirection.Left, plan.First.Direction);
        Assert.InRange(plan.ProjectedOffsetMs, -250, 250);
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
}
