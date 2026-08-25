using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;
using Maple.Core.Session;
using Maple.Core.Triggers;

namespace Maple.Host.Stationary;

public sealed class StationarySessionController(
    IStationaryActionSink actions,
    IStationarySafetyGate safety,
    IMonotonicScheduler scheduler,
    IStationaryConfigProvider configs,
    WeightedAttackDurationSampler attackSampler,
    StationaryMovementPlanner movementPlanner,
    IAttackTriggerStrategy trigger,
    IRandomSource random,
    IStationaryStatePublisher publisher,
    IStationaryMovementTelemetrySink? movementTelemetry = null)
{
    private const int AttackReleaseSettleMs = 100;
    private const int DirectionReleaseSettleMs = 100;

    public async Task RunAsync(
        Guid sessionId,
        MovementDirection initialFacing,
        int? cycleLimit,
        CancellationToken cancellationToken)
    {
        movementPlanner.StartSession(initialFacing);
        long cycleId = 0;
        string? stopReason = null;

        try
        {
            while (!cycleLimit.HasValue || cycleId < cycleLimit.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StationaryAttackConfig config = configs.GetValidatedSnapshot();
                ConfigValidationResult validation = StationaryConfigValidator.Validate(config);
                if (!validation.IsValid) throw new SessionStopException("CONFIG_INVALID");
                movementPlanner.ValidateCurrentOffset(config.MaxLateralMoveMs);
                AttackTriggerDecision decision = trigger.ShouldAttack(ObservationContext.Empty);
                if (!decision.ShouldAttack) throw new SessionStopException(decision.Code);

                cycleId++;
                AttackDurationSample attack = attackSampler.Sample(config.AttackBands);
                await HoldAsync(
                    sessionId,
                    cycleId,
                    StationaryPhase.AttackHolding,
                    StationaryInputAction.Attack,
                    attack.DurationMs,
                    attack.DurationMs,
                    cancellationToken);

                await DelayPhaseAsync(
                    sessionId,
                    cycleId,
                    StationaryPhase.AttackReleased,
                    AttackReleaseSettleMs,
                    attack.DurationMs,
                    cancellationToken);

                MovementCycle movement = movementPlanner.BeginCycle(config);
                MovementSegment? first = movementPlanner.TryCreateFirstSegment(config, movement);
                if (first is null)
                {
                    MovementSegment? recovery = movementPlanner.TryCreateRecoverySegment(config);
                    if (recovery is null)
                    {
                        Publish(
                            sessionId,
                            cycleId,
                            StationaryPhase.MoveSecond,
                            0,
                            attack.DurationMs,
                            "MOVEMENT_FROZEN_NO_SAFE_RECOVERY");
                    }
                    else
                    {
                        await HoldAsync(
                            sessionId,
                            cycleId,
                            StationaryPhase.MoveSecond,
                            ToInputAction(recovery.Direction),
                            recovery.HoldMs,
                            attack.DurationMs,
                            cancellationToken,
                            released => ApplyMovementResultAsync(
                                sessionId,
                                cycleId,
                                MovementIntent.RecoveryTowardCenter,
                                recovery,
                                released,
                                config));
                    }
                }
                else
                {
                    await HoldAsync(
                        sessionId,
                        cycleId,
                        StationaryPhase.MoveFirst,
                        ToInputAction(first.Direction),
                        first.HoldMs,
                        attack.DurationMs,
                        cancellationToken,
                        released => ApplyMovementResultAsync(
                            sessionId,
                            cycleId,
                            movement.Intent,
                            first,
                            released,
                            config));
                    int gapMs = checked(
                        DirectionReleaseSettleMs + movementPlanner.SampleGapMs(config));
                    await DelayPhaseAsync(sessionId, cycleId, StationaryPhase.MoveGap, gapMs, attack.DurationMs, cancellationToken);
                    MovementSegment second = movementPlanner.CreateSecondSegment(config, movement);
                    await HoldAsync(
                        sessionId,
                        cycleId,
                        StationaryPhase.MoveSecond,
                        ToInputAction(second.Direction),
                        second.HoldMs,
                        attack.DurationMs,
                        cancellationToken,
                        released => ApplyMovementResultAsync(
                            sessionId,
                            cycleId,
                            movement.Intent,
                            second,
                            released,
                            config));
                    movementPlanner.CompleteCycle(config, movement);
                }
                int stabilizeMs = movementPlanner.SampleStabilizeMs(config);
                await DelayPhaseAsync(sessionId, cycleId, StationaryPhase.Stabilizing, stabilizeMs, attack.DurationMs, cancellationToken);

                if (config.RestEnabled && random.NextInclusive(1, 100) <= config.RestProbabilityPercent)
                {
                    int restMs = random.NextInclusive(config.RestMinMs, config.RestMaxMs);
                    await DelayPhaseAsync(sessionId, cycleId, StationaryPhase.Resting, restMs, attack.DurationMs, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopReason = "CANCELLED";
        }
        catch (SessionStopException exception)
        {
            stopReason = exception.Code;
        }
        catch (InvalidOperationException exception) when (IsMovementStopCode(exception.Message))
        {
            stopReason = exception.Message;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            stopReason = "RUNTIME_EXCEPTION:" + exception.GetType().Name;
        }
        finally
        {
            InputActionResult release = await actions.ReleaseAllAsync(CancellationToken.None);
            if (!release.Success) stopReason = release.Code;
            Publish(sessionId, cycleId, StationaryPhase.Stopped, 0, 0, stopReason);
        }
    }

    private async Task<InputActionResult> HoldAsync(
        Guid sessionId,
        long cycleId,
        StationaryPhase phase,
        StationaryInputAction action,
        int holdMs,
        int sampledAttackDurationMs,
        CancellationToken cancellationToken,
        Func<InputActionResult, Task>? onReleased = null)
    {
        SafetyCheckResult gate = await safety.CheckAsync(cancellationToken);
        if (!gate.Success) throw new SessionStopException(gate.Code);

        Publish(sessionId, cycleId, phase, holdMs, sampledAttackDurationMs, null);
        InputActionResult down = await actions.KeyDownAsync(action, holdMs, cancellationToken);
        if (!down.Success) throw new SessionStopException(down.Code);

        InputActionResult? up = null;
        try
        {
            await DelayWithSafetyChecksAsync(holdMs, cancellationToken);
        }
        finally
        {
            up = await actions.KeyUpAsync(action, CancellationToken.None);
            if (!up.Success) throw new SessionStopException(up.Code);
            if (onReleased is not null) await onReleased(up);
        }
        return up!;
    }

    private async Task ApplyMovementResultAsync(
        Guid sessionId,
        long cycleId,
        MovementIntent intent,
        MovementSegment segment,
        InputActionResult result,
        StationaryAttackConfig config)
    {
        if (result.ActualHoldMs is not >= 1 or > StationaryAttackConfig.MovementDurationLimitMs ||
            result.ReleaseLatenessMs is null or < 0)
            throw new SessionStopException("MOVEMENT_TIMING_INVALID");
        int offsetBefore = movementPlanner.RelativeOffsetMs;
        movementPlanner.ApplyCompletedSegment(
            segment.Direction,
            result.ActualHoldMs.Value,
            config.MaxLateralMoveMs);
        if (movementTelemetry is not null)
        {
            await movementTelemetry.WriteAsync(
                new StationaryMovementTelemetry(
                    sessionId,
                    cycleId,
                    segment.Direction,
                    intent,
                    segment.HoldMs,
                    result.ActualHoldMs.Value,
                    result.ReleaseLatenessMs.Value,
                    offsetBefore,
                    movementPlanner.RelativeOffsetMs,
                    config.MaxLateralMoveMs),
                CancellationToken.None);
        }
    }

    private async Task DelayPhaseAsync(
        Guid sessionId,
        long cycleId,
        StationaryPhase phase,
        int durationMs,
        int sampledAttackDurationMs,
        CancellationToken cancellationToken)
    {
        Publish(sessionId, cycleId, phase, durationMs, sampledAttackDurationMs, null);
        await DelayWithSafetyChecksAsync(durationMs, cancellationToken);
    }

    private async Task DelayWithSafetyChecksAsync(int durationMs, CancellationToken cancellationToken)
    {
        const int safetyPollMs = 100;
        int remaining = durationMs;
        while (remaining > 0)
        {
            int slice = Math.Min(safetyPollMs, remaining);
            await scheduler.DelayAsync(slice, cancellationToken);
            remaining -= slice;
            if (remaining == 0) continue;
            SafetyCheckResult gate = await safety.CheckAsync(cancellationToken);
            if (!gate.Success) throw new SessionStopException(gate.Code);
        }
    }

    private void Publish(
        Guid sessionId,
        long cycleId,
        StationaryPhase phase,
        int durationMs,
        int sampledAttackDurationMs,
        string? earlyReleaseReason)
    {
        long start = scheduler.NowMonoMs;
        publisher.Publish(new StationaryRhythmState(
            StationaryAttackConfig.SchemaVersionCurrent,
            sessionId,
            cycleId,
            phase,
            sampledAttackDurationMs,
            start,
            start + durationMs,
            durationMs,
            start,
            earlyReleaseReason,
            movementPlanner.RelativeOffsetMs));
    }

    private static StationaryInputAction ToInputAction(MovementDirection direction) =>
        direction == MovementDirection.Left ? StationaryInputAction.MoveLeft : StationaryInputAction.MoveRight;

    private static bool IsMovementStopCode(string code) =>
        code.EndsWith("BUDGET_EXHAUSTED", StringComparison.Ordinal) ||
        code is "MOVEMENT_OFFSET_EXCEEDED" or "MOVEMENT_RETURN_UNSATISFIED";

    private sealed class SessionStopException(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }
}
