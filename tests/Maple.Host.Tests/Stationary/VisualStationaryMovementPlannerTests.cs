using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class VisualStationaryMovementPlannerTests
{
    [Theory]
    [InlineData(VisualSafetyState.GuardLeft, MovementDirection.Right)]
    [InlineData(VisualSafetyState.GuardRight, MovementDirection.Left)]
    public void Guard_states_only_allow_random_inward_movement(
        VisualSafetyState state,
        MovementDirection expected)
    {
        var random = new SequenceRandomSource(37);
        var planner = new VisualStationaryMovementPlanner(random);

        VisualMoveDecision decision = planner.Sample(Config(), PlatformState(state));

        Assert.True(decision.ShouldMove);
        Assert.Equal(expected, decision.Direction);
        Assert.Equal(37, decision.HoldMs);
    }

    [Fact]
    public void Safe_state_keeps_direction_and_duration_random()
    {
        var planner = new VisualStationaryMovementPlanner(new SequenceRandomSource(1, 34, 2, 46));

        VisualMoveDecision first = planner.Sample(Config(), PlatformState(VisualSafetyState.Safe));
        VisualMoveDecision second = planner.Sample(Config(), PlatformState(VisualSafetyState.Safe));

        Assert.Equal(MovementDirection.Left, first.Direction);
        Assert.Equal(34, first.HoldMs);
        Assert.Equal(MovementDirection.Right, second.Direction);
        Assert.Equal(46, second.HoldMs);
    }

    [Theory]
    [InlineData(VisualSafetyState.Untrusted)]
    [InlineData(VisualSafetyState.Outside)]
    public void Unsafe_states_freeze_movement(VisualSafetyState state)
    {
        var planner = new VisualStationaryMovementPlanner(new SequenceRandomSource());

        VisualMoveDecision decision = planner.Sample(Config(), PlatformState(state));

        Assert.False(decision.ShouldMove);
    }

    [Fact]
    public void Requested_outward_direction_is_rejected_in_guard_but_inward_duration_stays_random()
    {
        var planner = new VisualStationaryMovementPlanner(new SequenceRandomSource(41));
        VisualPlatformState leftGuard = PlatformState(VisualSafetyState.GuardLeft);

        VisualMoveDecision outward = planner.Authorize(Config(), leftGuard, MovementDirection.Left);
        VisualMoveDecision inward = planner.Authorize(Config(), leftGuard, MovementDirection.Right);

        Assert.False(outward.ShouldMove);
        Assert.True(inward.ShouldMove);
        Assert.Equal(41, inward.HoldMs);
    }

    private static StationaryAttackConfig Config() => StationaryAttackConfig.Default with
    {
        MoveHoldMinMs = 34,
        MoveHoldMaxMs = 46
    };

    private static VisualPlatformState PlatformState(VisualSafetyState state) =>
        new(state, 1, state == VisualSafetyState.Untrusted ? null : 250, 0.98, 32, 0, state.ToString());

    private sealed class SequenceRandomSource(params int[] values) : IRandomSource
    {
        private readonly Queue<int> remaining = new(values);

        public int NextInclusive(int minimum, int maximum)
        {
            int value = remaining.Dequeue();
            Assert.InRange(value, minimum, maximum);
            return value;
        }
    }
}
