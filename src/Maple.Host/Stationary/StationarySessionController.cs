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
    IStationaryStatePublisher publisher)
{
    private const int AttackReleaseSettleMs = 100;

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

                MovementPlan movement = movementPlanner.CreatePlan(config);
                await HoldAsync(
                    sessionId,
                    cycleId,
                    StationaryPhase.MoveFirst,
                    ToInputAction(movement.First.Direction),
                    movement.First.HoldMs,
                    attack.DurationMs,
                    cancellationToken);
                await DelayPhaseAsync(sessionId, cycleId, StationaryPhase.MoveGap, movement.GapMs, attack.DurationMs, cancellationToken);
                await HoldAsync(
                    sessionId,
                    cycleId,
                    StationaryPhase.MoveSecond,
                    ToInputAction(movement.Second.Direction),
                    movement.Second.HoldMs,
                    attack.DurationMs,
                    cancellationToken);
                movementPlanner.ApplyCompletedPlan(movement);
                await DelayPhaseAsync(sessionId, cycleId, StationaryPhase.Stabilizing, movement.StabilizeMs, attack.DurationMs, cancellationToken);

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
        catch (InvalidOperationException exception) when (
            exception.Message.EndsWith("BUDGET_EXHAUSTED", StringComparison.Ordinal))
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

    private async Task HoldAsync(
        Guid sessionId,
        long cycleId,
        StationaryPhase phase,
        StationaryInputAction action,
        int holdMs,
        int sampledAttackDurationMs,
        CancellationToken cancellationToken)
    {
        SafetyCheckResult gate = await safety.CheckAsync(cancellationToken);
        if (!gate.Success) throw new SessionStopException(gate.Code);

        Publish(sessionId, cycleId, phase, holdMs, sampledAttackDurationMs, null);
        InputActionResult down = await actions.KeyDownAsync(action, holdMs, cancellationToken);
        if (!down.Success) throw new SessionStopException(down.Code);

        try
        {
            await DelayWithSafetyChecksAsync(holdMs, cancellationToken);
        }
        finally
        {
            InputActionResult up = await actions.KeyUpAsync(action, CancellationToken.None);
            if (!up.Success) throw new SessionStopException(up.Code);
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
            earlyReleaseReason));
    }

    private static StationaryInputAction ToInputAction(MovementDirection direction) =>
        direction == MovementDirection.Left ? StationaryInputAction.MoveLeft : StationaryInputAction.MoveRight;

    private sealed class SessionStopException(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }
}
