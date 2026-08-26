using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class VisualFallbackMovementPlannerTests
{
    [Theory]
    [InlineData(MovementDirection.Left)]
    [InlineData(MovementDirection.Right)]
    public void Retains_the_most_recent_32_valid_samples_per_direction(MovementDirection direction)
    {
        var planner = new VisualFallbackMovementPlanner(new MinimumRandom(), platformWidthPx: 276);

        for (int displacementPx = 10; displacementPx <= 41; displacementPx++)
        {
            planner.RecordTrustedMovement(
                direction,
                actualHoldMs: 100,
                beforeCenterX: 100,
                afterCenterX: 100 + (int)direction * displacementPx);
        }

        Assert.Equal(32, SampleCount(planner, direction));
        Assert.Equal(0.255, MedianPixelsPerMs(planner, direction), 6);

        planner.RecordTrustedMovement(
            direction,
            actualHoldMs: 100,
            beforeCenterX: 100,
            afterCenterX: 100 + (int)direction * 50);

        Assert.Equal(32, SampleCount(planner, direction));
        Assert.Equal(0.265, MedianPixelsPerMs(planner, direction), 6);
    }

    [Fact]
    public void Returns_a_structured_result_for_an_accepted_observation()
    {
        var planner = new VisualFallbackMovementPlanner(new MinimumRandom(), platformWidthPx: 276);

        var result = planner.RecordTrustedMovement(
            MovementDirection.Right,
            actualHoldMs: 40,
            beforeCenterX: 100,
            afterCenterX: 120);

        Assert.True(result.Accepted);
        Assert.Equal("VISUAL_CALIBRATION_ACCEPTED", result.ResultCode);
        Assert.Equal(MovementDirection.Right, result.Direction);
        Assert.Equal(40, result.ActualHoldMs);
        Assert.Equal(100, result.BeforeCenterX);
        Assert.Equal(120, result.AfterCenterX);
        Assert.Equal(20, result.DisplacementPx);
        Assert.Equal(0.5, result.CandidatePixelsPerMs);
        Assert.Equal(0, result.LeftSampleCount);
        Assert.Equal(1, result.RightSampleCount);
        Assert.Null(result.LeftMedianPixelsPerMs);
        Assert.Equal(0.5, result.RightMedianPixelsPerMs);
    }

    [Fact]
    public void Returns_a_structured_result_for_a_rejected_observation_without_changing_samples()
    {
        var planner = new VisualFallbackMovementPlanner(new MinimumRandom(), platformWidthPx: 276);
        planner.RecordTrustedMovement(MovementDirection.Right, 40, 100, 120);

        var result = planner.RecordTrustedMovement(
            MovementDirection.Left,
            actualHoldMs: 40,
            beforeCenterX: 100,
            afterCenterX: 105);

        Assert.False(result.Accepted);
        Assert.Equal("VISUAL_CALIBRATION_DISPLACEMENT_INVALID", result.ResultCode);
        Assert.Equal(MovementDirection.Left, result.Direction);
        Assert.Equal(40, result.ActualHoldMs);
        Assert.Equal(100, result.BeforeCenterX);
        Assert.Equal(105, result.AfterCenterX);
        Assert.Equal(-5, result.DisplacementPx);
        Assert.Equal(-0.125, result.CandidatePixelsPerMs);
        Assert.Equal(0, result.LeftSampleCount);
        Assert.Equal(1, result.RightSampleCount);
        Assert.Null(result.LeftMedianPixelsPerMs);
        Assert.Equal(0.5, result.RightMedianPixelsPerMs);
    }

    [Theory]
    [InlineData(0, 80, "VISUAL_CALIBRATION_TIMING_INVALID")]
    [InlineData(40, -20, "VISUAL_CALIBRATION_RATE_INVALID")]
    public void Returns_structured_codes_for_timing_and_rate_rejections_without_changing_samples(
        int actualHoldMs,
        int afterCenterX,
        string expectedResultCode)
    {
        var planner = new VisualFallbackMovementPlanner(new MinimumRandom(), platformWidthPx: 276);
        planner.RecordTrustedMovement(MovementDirection.Right, 40, 100, 120);

        var result = planner.RecordTrustedMovement(
            MovementDirection.Left,
            actualHoldMs,
            beforeCenterX: 100,
            afterCenterX);

        Assert.False(result.Accepted);
        Assert.Equal(expectedResultCode, result.ResultCode);
        Assert.Equal(0, result.LeftSampleCount);
        Assert.Equal(1, result.RightSampleCount);
        Assert.Equal(0, planner.LeftSampleCount);
        Assert.Equal(1, planner.RightSampleCount);
    }

    [Fact]
    public void Starting_fallback_preserves_the_trusted_anchor_time_offset_in_the_public_snapshot()
    {
        VisualFallbackMovementPlanner planner = Calibrated(new MinimumRandom());

        planner.ObserveTrustedPosition(offsetPx: -12, guardWidthPx: 48, relativeOffsetMs: 31);
        Assert.True(planner.TryStartFallback(MovementDirection.Right));

        VisualFallbackProjectionSnapshot snapshot = Assert.IsType<VisualFallbackProjectionSnapshot>(
            planner.ProjectionSnapshot);
        Assert.Equal(-12, snapshot.OffsetPx);
        Assert.Equal(2, snapshot.UncertaintyPx);
        Assert.Equal(31, snapshot.RelativeOffsetMs);
    }

    [Fact]
    public void Ending_fallback_reanchors_the_projection_with_the_current_real_time_offset()
    {
        VisualFallbackMovementPlanner planner = Calibrated(new MinimumRandom());
        planner.ObserveTrustedPosition(offsetPx: 0, guardWidthPx: 48, relativeOffsetMs: 18);
        Assert.True(planner.TryStartFallback(MovementDirection.Right));

        planner.EndFallback(
            trustedOffsetPx: 7,
            trustedGuardWidthPx: 48,
            relativeOffsetMs: -23);

        VisualFallbackProjectionSnapshot snapshot = Assert.IsType<VisualFallbackProjectionSnapshot>(
            planner.ProjectionSnapshot);
        Assert.False(planner.IsFallbackActive);
        Assert.Equal(7, snapshot.OffsetPx);
        Assert.Equal(2, snapshot.UncertaintyPx);
        Assert.Equal(-23, snapshot.RelativeOffsetMs);
    }

    [Fact]
    public void Previewing_a_segment_reports_the_projection_without_mutating_planner_state()
    {
        VisualFallbackMovementPlanner planner = Calibrated(new MinimumRandom());
        planner.ObserveTrustedPosition(offsetPx: 5, guardWidthPx: 48, relativeOffsetMs: 7);
        Assert.True(planner.TryStartFallback(MovementDirection.Right));

        VisualFallbackProjectionSnapshot preview = planner.PreviewSegment(MovementDirection.Left, 40);
        VisualFallbackProjectionSnapshot current = Assert.IsType<VisualFallbackProjectionSnapshot>(
            planner.ProjectionSnapshot);

        Assert.Equal(-16, preview.OffsetPx, 3);
        Assert.Equal(7.25, preview.UncertaintyPx, 3);
        Assert.Equal(-33, preview.RelativeOffsetMs);
        Assert.Equal(5, current.OffsetPx);
        Assert.Equal(2, current.UncertaintyPx);
        Assert.Equal(7, current.RelativeOffsetMs);
        Assert.Equal(90, planner.UsableHalfWidthPx);
    }

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

    private static int SampleCount(
        VisualFallbackMovementPlanner planner,
        MovementDirection direction) =>
        direction == MovementDirection.Left ? planner.LeftSampleCount : planner.RightSampleCount;

    private static double MedianPixelsPerMs(
        VisualFallbackMovementPlanner planner,
        MovementDirection direction) =>
        (direction == MovementDirection.Left ? planner.LeftPixelsPerMs : planner.RightPixelsPerMs)!.Value;

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
