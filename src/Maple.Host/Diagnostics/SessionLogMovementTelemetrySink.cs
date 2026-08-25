using Maple.Host.Stationary;

namespace Maple.Host.Diagnostics;

public sealed class SessionLogMovementTelemetrySink(ISessionLog log) : IStationaryMovementTelemetrySink
{
    public async Task WriteAsync(
        StationaryMovementTelemetry telemetry,
        CancellationToken cancellationToken)
    {
        try
        {
            await log.WriteAsync(
                new SessionLogEntry(
                    DateTimeOffset.UtcNow,
                    telemetry.SessionId,
                    telemetry.CycleId,
                    "Movement",
                    "segmentCompleted",
                    "OK",
                    Direction: telemetry.Direction.ToString(),
                    MovementIntent: telemetry.Intent.ToString(),
                    RequestedHoldMs: telemetry.RequestedHoldMs,
                    ActualHoldMs: telemetry.ActualHoldMs,
                    ReleaseLatenessMs: telemetry.ReleaseLatenessMs,
                    OffsetBeforeMs: telemetry.OffsetBeforeMs,
                    OffsetAfterMs: telemetry.OffsetAfterMs,
                    MaxLateralMoveMs: telemetry.MaxLateralMoveMs),
                cancellationToken);
        }
        catch
        {
            // Diagnostic persistence must not interrupt an active input session.
        }
    }
}
