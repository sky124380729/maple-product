using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;

namespace Maple.Host.Stationary;

public sealed record VisualFallbackCycle(double StartOffsetPx, MovementIntent Intent);

public sealed record VisualFallbackCalibrationResult(
    bool Accepted,
    string ResultCode,
    MovementDirection Direction,
    int ActualHoldMs,
    double BeforeCenterX,
    double AfterCenterX,
    double DisplacementPx,
    double? CandidatePixelsPerMs,
    int LeftSampleCount,
    int RightSampleCount,
    double? LeftMedianPixelsPerMs,
    double? RightMedianPixelsPerMs);

public sealed record VisualFallbackProjectionSnapshot(
    double OffsetPx,
    double UncertaintyPx,
    int RelativeOffsetMs);

public sealed class VisualFallbackMovementPlanner(IRandomSource random, int platformWidthPx)
{
    public const int ReleaseSafetyMarginMs = 20;
    public const int RequiredSamplesPerDirection = 2;
    private const int MaximumSamplesPerDirection = 32;
    private const double MinimumDisplacementPx = 2;
    private const double MinimumPixelsPerMs = 0.05;
    private const double MaximumPixelsPerMs = 2.50;
    private const double InitialUncertaintyPx = 2;

    private readonly Queue<double> leftSamples = [];
    private readonly Queue<double> rightSamples = [];
    private Projection? estimate;
    private int guardWidthPx;
    private bool returnTowardCenterRequired;

    public int LeftSampleCount => leftSamples.Count;
    public int RightSampleCount => rightSamples.Count;
    public bool IsCalibrated =>
        LeftSampleCount >= RequiredSamplesPerDirection &&
        RightSampleCount >= RequiredSamplesPerDirection;
    public bool IsFallbackActive { get; private set; }
    public int RelativeOffsetMs => estimate?.RelativeOffsetMs ?? 0;
    public double? PredictedOffsetPx => estimate?.OffsetPx;
    public double UncertaintyPx => estimate?.UncertaintyPx ?? 0;
    public VisualFallbackProjectionSnapshot? ProjectionSnapshot => estimate is { } current
        ? new(current.OffsetPx, current.UncertaintyPx, current.RelativeOffsetMs)
        : null;
    public double? LeftPixelsPerMs => Median(leftSamples);
    public double? RightPixelsPerMs => Median(rightSamples);
    public MovementDirection InitialFacing { get; private set; }

    public VisualFallbackCalibrationResult RecordTrustedMovement(
        MovementDirection direction,
        int actualHoldMs,
        double beforeCenterX,
        double afterCenterX)
    {
        double displacement = (afterCenterX - beforeCenterX) * (int)direction;
        double? rate = actualHoldMs > 0 ? displacement / actualHoldMs : null;
        if (actualHoldMs <= 0 || actualHoldMs > StationaryAttackConfig.MovementDurationLimitMs)
            return CalibrationResult(
                accepted: false,
                "VISUAL_CALIBRATION_TIMING_INVALID",
                direction,
                actualHoldMs,
                beforeCenterX,
                afterCenterX,
                displacement,
                rate);
        if (displacement < MinimumDisplacementPx)
            return CalibrationResult(
                accepted: false,
                "VISUAL_CALIBRATION_DISPLACEMENT_INVALID",
                direction,
                actualHoldMs,
                beforeCenterX,
                afterCenterX,
                displacement,
                rate);
        if (!double.IsFinite(rate!.Value) || rate.Value is < MinimumPixelsPerMs or > MaximumPixelsPerMs)
            return CalibrationResult(
                accepted: false,
                "VISUAL_CALIBRATION_RATE_INVALID",
                direction,
                actualHoldMs,
                beforeCenterX,
                afterCenterX,
                displacement,
                rate);
        Queue<double> samples = direction == MovementDirection.Left ? leftSamples : rightSamples;
        samples.Enqueue(rate.Value);
        while (samples.Count > MaximumSamplesPerDirection) samples.Dequeue();
        return CalibrationResult(
            accepted: true,
            "VISUAL_CALIBRATION_ACCEPTED",
            direction,
            actualHoldMs,
            beforeCenterX,
            afterCenterX,
            displacement,
            rate);
    }

    public void ObserveTrustedPosition(int offsetPx, int guardWidthPx, int relativeOffsetMs = 0)
    {
        this.guardWidthPx = guardWidthPx;
        estimate = new Projection(offsetPx, InitialUncertaintyPx, relativeOffsetMs);
        IsFallbackActive = false;
        returnTowardCenterRequired = false;
    }

    public void TrackUnverifiedMovement(MovementDirection direction, int actualHoldMs)
    {
        if (estimate is null || !IsCalibrated || actualHoldMs <= 0) return;
        estimate = Apply(estimate.Value, direction, actualHoldMs, includeTimeOffset: false);
    }

    public bool TryStartFallback(MovementDirection initialFacing)
    {
        if (!IsCalibrated || estimate is null || !IsInsidePixelBoundary(estimate.Value)) return false;
        InitialFacing = initialFacing;
        IsFallbackActive = true;
        returnTowardCenterRequired = false;
        return true;
    }

    public void EndFallback(
        int trustedOffsetPx,
        int trustedGuardWidthPx,
        int relativeOffsetMs = 0)
    {
        ObserveTrustedPosition(trustedOffsetPx, trustedGuardWidthPx, relativeOffsetMs);
    }

    public void InvalidateFallbackAnchor()
    {
        estimate = null;
        IsFallbackActive = false;
        returnTowardCenterRequired = false;
    }

    public VisualFallbackCycle BeginCycle(StationaryAttackConfig config)
    {
        Projection current = RequireActive();
        ValidateCurrentState(config.MaxLateralMoveMs, current);
        double usableHalfWidth = UsableHalfWidthPx;
        double ratio = usableHalfWidth <= 0 ? 1 : Math.Abs(current.OffsetPx) / usableHalfWidth;
        MovementIntent intent = returnTowardCenterRequired
            ? MovementIntent.ReturnTowardCenter
            : ratio <= 0.40
                ? MovementIntent.Unbiased
                : ratio <= 0.70 && random.NextInclusive(1, 100) > 75
                    ? MovementIntent.Unbiased
                    : MovementIntent.ReturnTowardCenter;
        return new VisualFallbackCycle(current.OffsetPx, intent);
    }

    public MovementSegment? TryCreateFirstSegment(
        StationaryAttackConfig config,
        VisualFallbackCycle cycle)
    {
        Projection current = RequireActive();
        MovementDirection direction = FirstDirection();
        int[] candidates = SafeHolds(current, direction, config)
            .Where(hold => LeavesLegalSecondForAllowedActualRange(current, direction, hold, config, cycle))
            .ToArray();
        return TryCreateSegment(direction, candidates);
    }

    public MovementSegment? TryCreateRecoverySegment(StationaryAttackConfig config)
    {
        Projection current = RequireActive();
        int[] candidates = SafeHolds(current, InitialFacing, config)
            .Where(hold => IsRecoveryTowardCenterForAllowedActualRange(current, InitialFacing, hold))
            .ToArray();
        return TryCreateSegment(InitialFacing, candidates);
    }

    public MovementSegment CreateSecondSegment(
        StationaryAttackConfig config,
        VisualFallbackCycle cycle)
    {
        int[] candidates = LegalSecondHolds(RequireActive(), config, cycle).ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException("VISUAL_FALLBACK_SECOND_UNAVAILABLE");
        return new MovementSegment(InitialFacing, candidates[random.NextInclusive(0, candidates.Length - 1)]);
    }

    public void ApplyCompletedSegment(
        MovementDirection direction,
        int actualHoldMs,
        int maximumOffsetMs)
    {
        if (actualHoldMs <= 0 || actualHoldMs > StationaryAttackConfig.MovementDurationLimitMs)
            throw new InvalidOperationException("MOVEMENT_TIMING_INVALID");
        Projection updated = Apply(RequireActive(), direction, actualHoldMs, includeTimeOffset: true);
        ValidateCurrentState(maximumOffsetMs, updated);
        estimate = updated;
    }

    public void CompleteCycle(VisualFallbackCycle cycle)
    {
        Projection current = RequireActive();
        returnTowardCenterRequired = cycle.Intent == MovementIntent.ReturnTowardCenter &&
            Math.Abs(current.OffsetPx) > Math.Abs(cycle.StartOffsetPx);
    }

    private bool LeavesLegalSecondForAllowedActualRange(
        Projection current,
        MovementDirection direction,
        int plannedHoldMs,
        StationaryAttackConfig config,
        VisualFallbackCycle cycle)
    {
        for (int actual = plannedHoldMs; actual <= plannedHoldMs + ReleaseSafetyMarginMs; actual++)
        {
            Projection projected = Apply(current, direction, actual, includeTimeOffset: true);
            if (!IsInside(projected, config.MaxLateralMoveMs) ||
                !LegalSecondHolds(projected, config, cycle).Any())
                return false;
        }
        return true;
    }

    private IEnumerable<int> LegalSecondHolds(
        Projection current,
        StationaryAttackConfig config,
        VisualFallbackCycle cycle)
    {
        foreach (int hold in SafeHolds(current, InitialFacing, config))
        {
            bool legal = true;
            for (int actual = hold; actual <= hold + ReleaseSafetyMarginMs; actual++)
            {
                Projection projected = Apply(current, InitialFacing, actual, includeTimeOffset: true);
                if (!IsInside(projected, config.MaxLateralMoveMs) ||
                    cycle.Intent == MovementIntent.ReturnTowardCenter &&
                    Math.Abs(projected.OffsetPx) > Math.Abs(cycle.StartOffsetPx))
                {
                    legal = false;
                    break;
                }
            }
            if (legal) yield return hold;
        }
    }

    private IEnumerable<int> SafeHolds(
        Projection current,
        MovementDirection direction,
        StationaryAttackConfig config)
    {
        for (int hold = config.MoveHoldMinMs; hold <= config.MoveHoldMaxMs; hold++)
        {
            Projection minimum = Apply(current, direction, hold, includeTimeOffset: true);
            Projection maximum = Apply(
                current,
                direction,
                checked(hold + ReleaseSafetyMarginMs),
                includeTimeOffset: true);
            if (IsInside(minimum, config.MaxLateralMoveMs) && IsInside(maximum, config.MaxLateralMoveMs))
                yield return hold;
        }
    }

    private bool IsRecoveryTowardCenterForAllowedActualRange(
        Projection current,
        MovementDirection direction,
        int holdMs)
    {
        for (int actual = holdMs; actual <= holdMs + ReleaseSafetyMarginMs; actual++)
        {
            Projection projected = Apply(current, direction, actual, includeTimeOffset: true);
            if (Math.Abs(projected.OffsetPx) >= Math.Abs(current.OffsetPx)) return false;
        }
        return true;
    }

    private Projection Apply(
        Projection current,
        MovementDirection direction,
        int actualHoldMs,
        bool includeTimeOffset)
    {
        double rate = Rate(direction);
        double delta = rate * actualHoldMs;
        return new Projection(
            current.OffsetPx + (int)direction * delta,
            current.UncertaintyPx + Math.Max(2, delta * 0.25),
            includeTimeOffset
                ? checked(current.RelativeOffsetMs + (int)direction * actualHoldMs)
                : current.RelativeOffsetMs);
    }

    private bool IsInside(Projection projection, int maximumOffsetMs) =>
        Math.Abs((long)projection.RelativeOffsetMs) <= maximumOffsetMs &&
        IsInsidePixelBoundary(projection);

    private bool IsInsidePixelBoundary(Projection projection) =>
        Math.Abs(projection.OffsetPx) + projection.UncertaintyPx <= UsableHalfWidthPx;

    private void ValidateCurrentState(int maximumOffsetMs, Projection projection)
    {
        if (Math.Abs((long)projection.RelativeOffsetMs) > maximumOffsetMs)
            throw new InvalidOperationException("MOVEMENT_OFFSET_EXCEEDED");
        if (!IsInsidePixelBoundary(projection))
            throw new InvalidOperationException("VISUAL_PREDICTED_BOUNDARY_EXCEEDED");
    }

    private Projection RequireActive() => IsFallbackActive && estimate.HasValue
        ? estimate.Value
        : throw new InvalidOperationException("VISUAL_FALLBACK_NOT_ACTIVE");

    private double Rate(MovementDirection direction) =>
        (direction == MovementDirection.Left ? LeftPixelsPerMs : RightPixelsPerMs) ??
        throw new InvalidOperationException("VISUAL_FALLBACK_NOT_CALIBRATED");

    private double UsableHalfWidthPx => platformWidthPx / 2d - guardWidthPx;

    private MovementDirection FirstDirection() =>
        InitialFacing == MovementDirection.Left ? MovementDirection.Right : MovementDirection.Left;

    private MovementSegment? TryCreateSegment(MovementDirection direction, IReadOnlyList<int> candidates) =>
        candidates.Count == 0
            ? null
            : new MovementSegment(direction, candidates[random.NextInclusive(0, candidates.Count - 1)]);

    private VisualFallbackCalibrationResult CalibrationResult(
        bool accepted,
        string resultCode,
        MovementDirection direction,
        int actualHoldMs,
        double beforeCenterX,
        double afterCenterX,
        double displacementPx,
        double? candidatePixelsPerMs) =>
        new(
            accepted,
            resultCode,
            direction,
            actualHoldMs,
            beforeCenterX,
            afterCenterX,
            displacementPx,
            candidatePixelsPerMs,
            LeftSampleCount,
            RightSampleCount,
            LeftPixelsPerMs,
            RightPixelsPerMs);

    private static double? Median(IEnumerable<double> source)
    {
        double[] values = source.Order().ToArray();
        if (values.Length == 0) return null;
        int middle = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2
            : values[middle];
    }

    private readonly record struct Projection(
        double OffsetPx,
        double UncertaintyPx,
        int RelativeOffsetMs);
}
