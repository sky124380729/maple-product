using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;

namespace Maple.Core.Tests.Movement;

public sealed class StationaryMovementPlannerTests
{
    [Fact]
    public void Uses_actual_first_duration_before_planning_the_second_segment()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(10, 0));
        StationaryAttackConfig config = StationaryAttackConfig.Default;
        planner.StartSession(MovementDirection.Right);

        MovementCycle cycle = planner.BeginCycle(config);
        MovementSegment first = planner.CreateFirstSegment(config, cycle);
        planner.ApplyCompletedSegment(first.Direction, actualHoldMs: 46, config.MaxLateralMoveMs);
        MovementSegment second = planner.CreateSecondSegment(config, cycle);

        Assert.Equal(20, StationaryMovementPlanner.ReleaseSafetyMarginMs);
        Assert.Equal(40, first.HoldMs);
        Assert.Equal(-46, planner.RelativeOffsetMs);
        Assert.Equal(MovementDirection.Right, second.Direction);
        Assert.Equal(30, second.HoldMs);
    }

    [Fact]
    public void First_segment_release_margin_still_leaves_a_legal_second_segment()
    {
        var planner = new StationaryMovementPlanner(new MaximumRandomSource());
        StationaryAttackConfig config = StationaryAttackConfig.Default with
        {
            MaxLateralMoveMs = 80,
            MoveHoldMinMs = 34,
            MoveHoldMaxMs = 46
        };
        planner.StartSession(MovementDirection.Right, relativeOffsetMs: -12);

        MovementCycle cycle = planner.BeginCycle(config);
        MovementSegment first = planner.CreateFirstSegment(config, cycle);
        planner.ApplyCompletedSegment(
            first.Direction,
            first.HoldMs + StationaryMovementPlanner.ReleaseSafetyMarginMs,
            config.MaxLateralMoveMs);
        MovementSegment second = planner.CreateSecondSegment(config, cycle);

        Assert.Equal(MovementDirection.Left, first.Direction);
        Assert.Equal(MovementDirection.Right, second.Direction);
        Assert.InRange(second.HoldMs, config.MoveHoldMinMs, config.MoveHoldMaxMs);
    }

    [Theory]
    [InlineData(32, null, MovementIntent.Unbiased)]
    [InlineData(33, 75, MovementIntent.ReturnTowardCenter)]
    [InlineData(56, 76, MovementIntent.Unbiased)]
    [InlineData(57, null, MovementIntent.ReturnTowardCenter)]
    public void Selects_intent_at_the_configured_zone_boundaries(
        int offset,
        int? roll,
        MovementIntent expected)
    {
        var planner = new StationaryMovementPlanner(
            roll.HasValue ? new SequenceRandomSource(roll.Value) : new SequenceRandomSource());
        planner.StartSession(MovementDirection.Right, offset);

        MovementCycle cycle = planner.BeginCycle(StationaryAttackConfig.Default);

        Assert.Equal(expected, cycle.Intent);
    }

    [Fact]
    public void Return_intent_keeps_multiple_random_nonzero_landings()
    {
        StationaryAttackConfig config = CompactConfig();

        int firstLanding = RunReturnCycle(config, secondCandidateIndex: 0);
        int secondLanding = RunReturnCycle(config, secondCandidateIndex: 10);

        Assert.Equal(50, firstLanding);
        Assert.Equal(60, secondLanding);
        Assert.NotEqual(firstLanding, secondLanding);
    }

    [Fact]
    public void Leaves_minimum_hold_and_margin_for_the_next_fixed_first_direction()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(20, 20));
        StationaryAttackConfig config = StationaryAttackConfig.Default;
        planner.StartSession(MovementDirection.Right);
        MovementCycle cycle = planner.BeginCycle(config);
        MovementSegment first = planner.CreateFirstSegment(config, cycle);
        planner.ApplyCompletedSegment(first.Direction, first.HoldMs, config.MaxLateralMoveMs);
        MovementSegment second = planner.CreateSecondSegment(config, cycle);
        planner.ApplyCompletedSegment(second.Direction, second.HoldMs, config.MaxLateralMoveMs);
        planner.CompleteCycle(config, cycle);

        int remainingForNextLeft = planner.RelativeOffsetMs + config.MaxLateralMoveMs;
        Assert.True(remainingForNextLeft >= config.MoveHoldMinMs + StationaryMovementPlanner.ReleaseSafetyMarginMs);
    }

    [Fact]
    public void Stops_when_the_fixed_first_direction_has_no_safe_candidate()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource());
        StationaryAttackConfig config = StationaryAttackConfig.Default with { MaxLateralMoveMs = 50 };
        planner.StartSession(MovementDirection.Right, relativeOffsetMs: -1);
        MovementCycle cycle = planner.BeginCycle(config);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => planner.CreateFirstSegment(config, cycle));

        Assert.Equal("INITIAL_FACING_BUDGET_EXHAUSTED", exception.Message);
    }

    [Fact]
    public void Creates_a_random_safe_recovery_when_the_fixed_first_direction_is_unavailable()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(7));
        StationaryAttackConfig config = StationaryAttackConfig.Default with
        {
            MaxLateralMoveMs = 80,
            MoveHoldMinMs = 34,
            MoveHoldMaxMs = 46
        };
        planner.StartSession(MovementDirection.Right, relativeOffsetMs: -22);
        MovementCycle cycle = planner.BeginCycle(config);

        MovementSegment? first = planner.TryCreateFirstSegment(config, cycle);
        MovementSegment? recovery = planner.TryCreateRecoverySegment(config);

        Assert.Null(first);
        Assert.NotNull(recovery);
        Assert.Equal(MovementDirection.Right, recovery.Direction);
        Assert.Equal(41, recovery.HoldMs);
    }

    [Fact]
    public void Returns_no_recovery_when_the_initial_facing_direction_has_no_safe_hold()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource());
        StationaryAttackConfig config = StationaryAttackConfig.Default with
        {
            MaxLateralMoveMs = 80,
            MoveHoldMinMs = 34,
            MoveHoldMaxMs = 46
        };
        planner.StartSession(MovementDirection.Right, relativeOffsetMs: 80);

        MovementSegment? recovery = planner.TryCreateRecoverySegment(config);

        Assert.Null(recovery);
    }

    [Fact]
    public void Completes_a_safe_cycle_even_when_the_next_fixed_first_direction_is_unavailable()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource());
        StationaryAttackConfig config = StationaryAttackConfig.Default with
        {
            MaxLateralMoveMs = 80,
            MoveHoldMinMs = 34,
            MoveHoldMaxMs = 46
        };
        planner.StartSession(MovementDirection.Right);
        MovementCycle cycle = planner.BeginCycle(config);
        planner.ApplyCompletedSegment(MovementDirection.Left, actualHoldMs: 30, config.MaxLateralMoveMs);

        planner.CompleteCycle(config, cycle);

        Assert.Equal(-30, planner.RelativeOffsetMs);
    }

    [Fact]
    public void Stops_when_actual_first_duration_leaves_no_legal_second_segment()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(0));
        StationaryAttackConfig config = StationaryAttackConfig.Default with { MoveHoldMaxMs = 40 };
        planner.StartSession(MovementDirection.Right);
        MovementCycle cycle = planner.BeginCycle(config);
        MovementSegment first = planner.CreateFirstSegment(config, cycle);
        planner.ApplyCompletedSegment(first.Direction, actualHoldMs: 80, config.MaxLateralMoveMs);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => planner.CreateSecondSegment(config, cycle));

        Assert.Equal("MOVEMENT_BUDGET_EXHAUSTED", exception.Message);
    }

    [Fact]
    public void Rejects_an_actual_segment_that_crosses_the_hard_boundary_without_mutating_offset()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource());
        planner.StartSession(MovementDirection.Right);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => planner.ApplyCompletedSegment(MovementDirection.Left, 81, maximumOffsetMs: 80));

        Assert.Equal("MOVEMENT_OFFSET_EXCEEDED", exception.Message);
        Assert.Equal(0, planner.RelativeOffsetMs);
    }

    [Fact]
    public void Allows_a_completed_return_cycle_that_keeps_absolute_offset_stable()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(1, 10, 0));
        StationaryAttackConfig config = CompactConfig();
        planner.StartSession(MovementDirection.Right, relativeOffsetMs: 60);
        MovementCycle cycle = planner.BeginCycle(config);
        MovementSegment first = planner.CreateFirstSegment(config, cycle);
        planner.ApplyCompletedSegment(first.Direction, actualHoldMs: 50, config.MaxLateralMoveMs);
        MovementSegment second = planner.CreateSecondSegment(config, cycle);
        planner.ApplyCompletedSegment(second.Direction, actualHoldMs: 50, config.MaxLateralMoveMs);

        planner.CompleteCycle(config, cycle);

        Assert.Equal(60, planner.RelativeOffsetMs);
    }

    [Fact]
    public void Return_planning_allows_a_random_landing_equal_to_the_cycle_start_offset()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(1, 0, 0));
        StationaryAttackConfig config = CompactConfig() with { MoveHoldMaxMs = 60 };
        planner.StartSession(MovementDirection.Right, relativeOffsetMs: 60);
        MovementCycle cycle = planner.BeginCycle(config);
        MovementSegment first = planner.CreateFirstSegment(config, cycle);
        planner.ApplyCompletedSegment(first.Direction, first.HoldMs, config.MaxLateralMoveMs);

        MovementSegment second = planner.CreateSecondSegment(config, cycle);

        Assert.Equal(10, first.HoldMs);
        Assert.Equal(10, second.HoldMs);
    }

    [Fact]
    public void Keeps_running_and_forces_the_next_return_when_release_jitter_worsens_a_return_cycle()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(1, 10, 0));
        StationaryAttackConfig config = CompactConfig();
        planner.StartSession(MovementDirection.Right, relativeOffsetMs: 60);
        MovementCycle cycle = planner.BeginCycle(config);
        MovementSegment first = planner.CreateFirstSegment(config, cycle);
        planner.ApplyCompletedSegment(first.Direction, actualHoldMs: 50, config.MaxLateralMoveMs);
        MovementSegment second = planner.CreateSecondSegment(config, cycle);
        planner.ApplyCompletedSegment(second.Direction, actualHoldMs: 51, config.MaxLateralMoveMs);

        planner.CompleteCycle(config, cycle);
        MovementCycle next = planner.BeginCycle(config);

        Assert.Equal(61, planner.RelativeOffsetMs);
        Assert.Equal(MovementIntent.ReturnTowardCenter, next.Intent);
    }

    [Fact]
    public void Keeps_ten_thousand_measured_jitter_cycles_bounded_and_variable()
    {
        var planner = new StationaryMovementPlanner(new CyclingRandomSource());
        StationaryAttackConfig config = StationaryAttackConfig.Default;
        planner.StartSession(MovementDirection.Right);
        var outcomes = new HashSet<(int First, int Second, int Offset)>();

        for (int index = 0; index < 10_000; index++)
        {
            MovementCycle cycle = planner.BeginCycle(config);
            MovementSegment first = planner.CreateFirstSegment(config, cycle);
            int actualFirst = first.HoldMs + (cycle.Intent == MovementIntent.Unbiased ? index % 21 : 0);
            Assert.InRange(actualFirst, first.HoldMs, first.HoldMs + StationaryMovementPlanner.ReleaseSafetyMarginMs);
            planner.ApplyCompletedSegment(first.Direction, actualFirst, config.MaxLateralMoveMs);
            Assert.InRange(planner.RelativeOffsetMs, -config.MaxLateralMoveMs, config.MaxLateralMoveMs);
            MovementSegment second = planner.CreateSecondSegment(config, cycle);
            int actualSecond = second.HoldMs + (cycle.Intent == MovementIntent.Unbiased ? (index * 7 + 3) % 21 : 0);
            Assert.InRange(actualSecond, second.HoldMs, second.HoldMs + StationaryMovementPlanner.ReleaseSafetyMarginMs);
            planner.ApplyCompletedSegment(second.Direction, actualSecond, config.MaxLateralMoveMs);
            planner.CompleteCycle(config, cycle);
            Assert.InRange(planner.RelativeOffsetMs, -config.MaxLateralMoveMs, config.MaxLateralMoveMs);
            outcomes.Add((actualFirst, actualSecond, planner.RelativeOffsetMs));
        }

        Assert.True(outcomes.Count >= 20, $"Expected varied movement, got {outcomes.Count} outcomes.");
    }

    [Fact]
    public void Keeps_offset_across_cycles_and_resets_for_a_new_session()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource());
        planner.StartSession(MovementDirection.Right);
        planner.ApplyCompletedSegment(MovementDirection.Left, 35, maximumOffsetMs: 80);

        Assert.Equal(-35, planner.RelativeOffsetMs);

        planner.StartSession(MovementDirection.Left);
        Assert.Equal(0, planner.RelativeOffsetMs);
    }

    [Fact]
    public void Handles_the_full_valid_lateral_budget_range_without_integer_overflow()
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(0, 0));
        StationaryAttackConfig config = StationaryAttackConfig.Default with
        {
            MaxLateralMoveMs = int.MaxValue
        };
        planner.StartSession(MovementDirection.Right);
        MovementCycle cycle = planner.BeginCycle(config);

        MovementSegment first = planner.CreateFirstSegment(config, cycle);
        planner.ApplyCompletedSegment(first.Direction, first.HoldMs, config.MaxLateralMoveMs);
        MovementSegment second = planner.CreateSecondSegment(config, cycle);

        Assert.Equal(MovementDirection.Right, second.Direction);
        Assert.InRange(second.HoldMs, config.MoveHoldMinMs, config.MoveHoldMaxMs);
    }

    private static int RunReturnCycle(StationaryAttackConfig config, int secondCandidateIndex)
    {
        var planner = new StationaryMovementPlanner(new SequenceRandomSource(1, 10, secondCandidateIndex));
        planner.StartSession(MovementDirection.Right, relativeOffsetMs: 60);
        MovementCycle cycle = planner.BeginCycle(config);
        MovementSegment first = planner.CreateFirstSegment(config, cycle);
        planner.ApplyCompletedSegment(first.Direction, first.HoldMs, config.MaxLateralMoveMs);
        MovementSegment second = planner.CreateSecondSegment(config, cycle);
        planner.ApplyCompletedSegment(second.Direction, second.HoldMs, config.MaxLateralMoveMs);
        planner.CompleteCycle(config, cycle);
        return planner.RelativeOffsetMs;
    }

    private static StationaryAttackConfig CompactConfig() => StationaryAttackConfig.Default with
    {
        MaxLateralMoveMs = 100,
        MoveHoldMinMs = 10,
        MoveHoldMaxMs = 30
    };

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

    private sealed class CyclingRandomSource : IRandomSource
    {
        private int value;

        public int NextInclusive(int minimum, int maximum)
        {
            int range = maximum - minimum + 1;
            return minimum + (value++ % range);
        }
    }

    private sealed class MaximumRandomSource : IRandomSource
    {
        public int NextInclusive(int minimum, int maximum) => maximum;
    }
}
