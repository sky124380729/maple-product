using Maple.Host.Stationary;

namespace Maple.Host.Diagnostics;

public sealed class SessionLogVisualFallbackTelemetrySink(ISessionLog log) : IVisualFallbackTelemetrySink
{
    public async Task WriteAsync(
        VisualFallbackTelemetry telemetry,
        CancellationToken cancellationToken)
    {
        try
        {
            await log.WriteAsync(
                new SessionLogEntry(
                    DateTimeOffset.UtcNow,
                    telemetry.SessionId,
                    telemetry.CycleId,
                    "VisualFallback",
                    telemetry.Event,
                    telemetry.ResultCode,
                    Direction: telemetry.Direction?.ToString(),
                    MovementIntent: telemetry.Intent?.ToString(),
                    RequestedHoldMs: telemetry.RequestedHoldMs,
                    ActualHoldMs: telemetry.ActualHoldMs,
                    OffsetBeforeMs: telemetry.OffsetBeforeMs,
                    OffsetAfterMs: telemetry.OffsetAfterMs,
                    MaxLateralMoveMs: telemetry.MaxLateralMoveMs,
                    PlannerKind: telemetry.PlannerKind,
                    OffsetBeforePx: telemetry.OffsetBeforePx,
                    OffsetAfterPx: telemetry.OffsetAfterPx,
                    UncertaintyBeforePx: telemetry.UncertaintyBeforePx,
                    UncertaintyAfterPx: telemetry.UncertaintyAfterPx,
                    UsableHalfWidthPx: telemetry.UsableHalfWidthPx,
                    CandidatePixelsPerMs: telemetry.CandidatePixelsPerMs,
                    LeftSampleCount: telemetry.LeftSampleCount,
                    RightSampleCount: telemetry.RightSampleCount,
                    LeftMedianPixelsPerMs: telemetry.LeftMedianPixelsPerMs,
                    RightMedianPixelsPerMs: telemetry.RightMedianPixelsPerMs,
                    DisplacementPx: telemetry.DisplacementPx,
                    BoundaryResult: telemetry.BoundaryResult),
                cancellationToken);
        }
        catch
        {
            // Diagnostic persistence must not interrupt an active input session.
        }
    }
}
