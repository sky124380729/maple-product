using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class VisualFallbackMovementPlannerTests
{
    [Fact]
    public void Requires_two_valid_samples_per_direction_and_uses_direction_medians()
    {
        var planner = new VisualFallbackMovementPlanner(new MinimumRandom(), platformWidthPx: 276);

        planner.RecordTrustedMovement(MovementDirection.Left, 40, 100, 80);
        planner.RecordTrustedMovement(MovementDirection.Left, 40, 100, 76);
        planner.RecordTrustedMovement(MovementDirection.Right, 40, 100, 116);
        Assert.False(planner.IsCalibrated);

        planner.RecordTrustedMovement(MovementDirection.Right, 40, 100, 132);

        Assert.True(planner.IsCalibrated);
        Assert.Equal(0.55, planner.LeftPixelsPerMs!.Value, 3);
        Assert.Equal(0.60, planner.RightPixelsPerMs!.Value, 3);
    }

    [Fact]
    public void Rejects_reverse_tiny_and_implausible_calibration_samples()
    {
        var planner = new VisualFallbackMovementPlanner(new MinimumRandom(), platformWidthPx: 276);

        planner.RecordTrustedMovement(MovementDirection.Left, 40, 100, 101);
        planner.RecordTrustedMovement(MovementDirection.Left, 40, 100, 99);
        planner.RecordTrustedMovement(MovementDirection.Left, 40, 100, -20);
        planner.RecordTrustedMovement(MovementDirection.Right, 40, 100, 101);
        planner.RecordTrustedMovement(MovementDirection.Right, 0, 100, 120);

        Assert.Equal(0, planner.LeftSampleCount);
        Assert.Equal(0, planner.RightSampleCount);
    }

    [Fact]
    public void Near_left_edge_skips_outward_first_and_returns_a_random_inward_recovery()
    {
        VisualFallbackMovementPlanner planner = Calibrated(new MinimumRandom());
        planner.ObserveTrustedPosition(offsetPx: -58, guardWidthPx: 48);
        Assert.True(planner.TryStartFallback(MovementDirection.Right));
        StationaryAttackConfig config = Config(maxLateralMoveMs: 80);

        VisualFallbackCycle cycle = planner.BeginCycle(config);
        MovementSegment? first = planner.TryCreateFirstSegment(config, cycle);
        MovementSegment? recovery = planner.TryCreateRecoverySegment(config);

        Assert.Null(first);
        Assert.NotNull(recovery);
        Assert.Equal(MovementDirection.Right, recovery!.Direction);
        Assert.InRange(recovery.HoldMs, config.MoveHoldMinMs, config.MoveHoldMaxMs);
    }

    [Fact]
    public void Every_planned_pair_stays_inside_pixel_and_time_boundaries_with_release_margin()
    {
        VisualFallbackMovementPlanner planner = Calibrated(new CyclingRandom());
        planner.ObserveTrustedPosition(offsetPx: 0, guardWidthPx: 48);
        Assert.True(planner.TryStartFallback(MovementDirection.Right));
        StationaryAttackConfig config = Config(maxLateralMoveMs: 80);

        for (int cycleNumber = 0; cycleNumber < 100; cycleNumber++)
        {
            VisualFallbackCycle cycle = planner.BeginCycle(config);
            MovementSegment? first = planner.TryCreateFirstSegment(config, cycle);
            if (first is null)
            {
                MovementSegment? recovery = planner.TryCreateRecoverySegment(config);
                if (recovery is not null)
                    planner.ApplyCompletedSegment(recovery.Direction, recovery.HoldMs + 20, config.MaxLateralMoveMs);
                continue;
            }

            planner.ApplyCompletedSegment(first.Direction, first.HoldMs + 20, config.MaxLateralMoveMs);
            MovementSegment second = planner.CreateSecondSegment(config, cycle);
            planner.ApplyCompletedSegment(second.Direction, second.HoldMs + 20, config.MaxLateralMoveMs);
            planner.CompleteCycle(cycle);

            Assert.InRange(Math.Abs(planner.RelativeOffsetMs), 0, config.MaxLateralMoveMs);
            Assert.True(
                Math.Abs(planner.PredictedOffsetPx!.Value) + planner.UncertaintyPx <= 90.0001,
                $"cycle={cycleNumber}, offset={planner.PredictedOffsetPx}, uncertainty={planner.UncertaintyPx}");
        }
    }

    [Fact]
    public void Large_visual_offset_selects_a_pair_that_finishes_no_farther_from_center()
    {
        VisualFallbackMovementPlanner planner = Calibrated(new MinimumRandom());
        planner.ObserveTrustedPosition(offsetPx: -58, guardWidthPx: 48);
        Assert.True(planner.TryStartFallback(MovementDirection.Left));
        StationaryAttackConfig config = Config(maxLateralMoveMs: 80);
        VisualFallbackCycle cycle = planner.BeginCycle(config);

        MovementSegment first = Assert.IsType<MovementSegment>(planner.TryCreateFirstSegment(config, cycle));
        planner.ApplyCompletedSegment(first.Direction, first.HoldMs, config.MaxLateralMoveMs);
        MovementSegment second = planner.CreateSecondSegment(config, cycle);
        planner.ApplyCompletedSegment(second.Direction, second.HoldMs, config.MaxLateralMoveMs);

        Assert.Equal(MovementIntent.ReturnTowardCenter, cycle.Intent);
        Assert.True(Math.Abs(planner.PredictedOffsetPx!.Value) <= Math.Abs(cycle.StartOffsetPx));
    }

    [Fact]
    public void Freezes_when_uncertainty_consumes_the_remaining_platform_space()
    {
        VisualFallbackMovementPlanner planner = Calibrated(new MinimumRandom());
        planner.ObserveTrustedPosition(offsetPx: 0, guardWidthPx: 137);
        Assert.False(planner.TryStartFallback(MovementDirection.Right));
    }

    private static VisualFallbackMovementPlanner Calibrated(IRandomSource random)
    {
        var planner = new VisualFallbackMovementPlanner(random, platformWidthPx: 276);
        planner.RecordTrustedMovement(MovementDirection.Left, 40, 100, 80);
        planner.RecordTrustedMovement(MovementDirection.Left, 40, 100, 78);
        planner.RecordTrustedMovement(MovementDirection.Right, 40, 100, 120);
        planner.RecordTrustedMovement(MovementDirection.Right, 40, 100, 122);
        return planner;
    }

    private static StationaryAttackConfig Config(int maxLateralMoveMs) => StationaryAttackConfig.Default with
    {
        AttackTriggerMode = AttackTriggerMode.VisualSafeContinuous,
        AttackBands = [new AttackBand(1, 1, 100), new(2, 2, 0), new(3, 3, 0), new(4, 4, 0)],
        MaxLateralMoveMs = maxLateralMoveMs,
        MoveHoldMinMs = 30,
        MoveHoldMaxMs = 50,
        RestEnabled = false
    };

    private sealed class MinimumRandom : IRandomSource
    {
        public int NextInclusive(int minimum, int maximum) => minimum;
    }

    private sealed class CyclingRandom : IRandomSource
    {
        private int value;
        public int NextInclusive(int minimum, int maximum)
        {
            int width = maximum - minimum + 1;
            int result = minimum + value % width;
            value++;
            return result;
        }
    }
}
