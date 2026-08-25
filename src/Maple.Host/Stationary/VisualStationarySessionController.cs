using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;
using Maple.Core.Session;

namespace Maple.Host.Stationary;

public sealed class VisualStationarySessionController(
    IStationaryActionSink actions,
    IStationarySafetyGate safety,
    IMonotonicScheduler scheduler,
    IStationaryConfigProvider configs,
    WeightedAttackDurationSampler attackSampler,
    VisualStationaryMovementPlanner movementPlanner,
    VisualFallbackMovementPlanner fallbackPlanner,
    IVisualStationaryObservationSource observations,
    IRandomSource random,
    IStationaryStatePublisher rhythmPublisher,
    IVisualStationaryStatePublisher visualPublisher)
{
    private const int AttackReleaseSettleMs = 100;
    private const int DirectionReleaseSettleMs = 100;
    private const int VisualSafetyPollIntervalMs = 100;
    private static readonly TimeSpan VisualFeedbackTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan MaximumObservationAge = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan FallbackDelay = TimeSpan.FromSeconds(15);
    private int relativeOffsetMs;
    private bool facingRestorePending;
    private readonly StationaryMovementPlanner continuousMovementPlanner = new(random);
    private bool continuousFallbackActive;
    private long? visualUnavailableSinceMonoMs;

    public async Task RunAsync(
        Guid sessionId,
        MovementDirection initialFacing,
        int? cycleLimit,
        CancellationToken cancellationToken)
    {
        long cycleId = 0;
        string? stopReason = null;
        try
        {
            while (!cycleLimit.HasValue || cycleId < cycleLimit.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StationaryAttackConfig config = configs.GetValidatedSnapshot();
                if (!StationaryConfigValidator.Validate(config).IsValid ||
                    config.AttackTriggerMode != AttackTriggerMode.VisualSafeContinuous)
                    throw new VisualSessionStopException("CONFIG_INVALID");

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

                bool useFallback = SelectMovementStrategy(
                    sessionId,
                    cycleId,
                    initialFacing,
                    config);
                if (useFallback)
                    await RunFallbackMovementCycleAsync(
                        sessionId,
                        cycleId,
                        attack.DurationMs,
                        config,
                        cancellationToken);
                else
                    await RunVisualMovementCycleAsync(
                        sessionId,
                        cycleId,
                        initialFacing,
                        attack.DurationMs,
                        config,
                        cancellationToken);

                if (config.RestEnabled && random.NextInclusive(1, 100) <= config.RestProbabilityPercent)
                {
                    int restMs = random.NextInclusive(config.RestMinMs, config.RestMaxMs);
                    await DelayPhaseAsync(
                        sessionId,
                        cycleId,
                        StationaryPhase.Resting,
                        restMs,
                        attack.DurationMs,
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopReason = "CANCELLED";
        }
        catch (VisualSessionStopException exception)
        {
            stopReason = exception.Code;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            stopReason = "RUNTIME_EXCEPTION:" + exception.GetType().Name;
        }
        finally
        {
            InputActionResult released = await actions.ReleaseAllAsync(CancellationToken.None);
            if (!released.Success) stopReason = released.Code;
            PublishRhythm(sessionId, cycleId, StationaryPhase.Stopped, 0, 0, stopReason);
            PublishVisual(sessionId, cycleId, observations.Latest, stopReason ?? "VISUAL_SESSION_STOPPED");
        }
    }

    private bool SelectMovementStrategy(
        Guid sessionId,
        long cycleId,
        MovementDirection initialFacing,
        StationaryAttackConfig config)
    {
        VisualStationaryObservation? current = observations.Latest;
        if (current is null || !observations.IsLatestFresh(MaximumObservationAge))
        {
            PublishVisual(sessionId, cycleId, current, "VISUAL_OBSERVATION_STALE", "Untrusted");
            return TryActivateContinuousFallback(sessionId, cycleId, initialFacing, current, config);
        }

        if (current.IdentityTrusted)
        {
            bool recovered = continuousFallbackActive || fallbackPlanner.IsFallbackActive;
            continuousFallbackActive = false;
            visualUnavailableSinceMonoMs = null;
            if (current.Platform.OffsetFromCenterPx.HasValue)
            {
                fallbackPlanner.EndFallback(
                    current.Platform.OffsetFromCenterPx.Value,
                    current.Platform.GuardWidthPx);
                if (recovered)
                {
                    PublishVisual(
                        sessionId,
                        cycleId,
                        current,
                        current.Platform.State == VisualSafetyState.Outside
                            ? "VISUAL_OUTSIDE_FROZEN"
                            : "VISUAL_FALLBACK_RECOVERED");
                }
            }
            if (current.Platform.State == VisualSafetyState.Outside) return false;
            return false;
        }

        PublishVisual(sessionId, cycleId, current, current.Code, "Untrusted");
        return TryActivateContinuousFallback(sessionId, cycleId, initialFacing, current, config);
    }

    private bool TryActivateContinuousFallback(
        Guid sessionId,
        long cycleId,
        MovementDirection initialFacing,
        VisualStationaryObservation? current,
        StationaryAttackConfig config)
    {
        visualUnavailableSinceMonoMs ??= scheduler.NowMonoMs;
        bool fallbackDue = observations.IsContinuouslyUntrustedFor(FallbackDelay) ||
            scheduler.NowMonoMs - visualUnavailableSinceMonoMs.Value >= FallbackDelay.TotalMilliseconds;
        if (!fallbackDue)
        {
            if (!continuousFallbackActive && fallbackPlanner.IsFallbackActive)
                fallbackPlanner.InvalidateFallbackAnchor();
            return false;
        }
        if (!continuousFallbackActive)
        {
            int boundedOffsetMs = Math.Clamp(
                relativeOffsetMs,
                -config.MaxLateralMoveMs,
                config.MaxLateralMoveMs);
            continuousMovementPlanner.StartSession(initialFacing, boundedOffsetMs);
            continuousFallbackActive = true;
            _ = fallbackPlanner.TryStartFallback(initialFacing);
        }
        PublishFallbackVisual(sessionId, cycleId, "VISUAL_FALLBACK_CONTINUOUS");
        return true;
    }

    private async Task RunVisualMovementCycleAsync(
        Guid sessionId,
        long cycleId,
        MovementDirection initialFacing,
        int sampledAttackDurationMs,
        StationaryAttackConfig config,
        CancellationToken cancellationToken)
    {
        VisualStationaryObservation? current = observations.Latest;
        MovementDirection? correction = current is { IdentityTrusted: true }
            ? movementPlanner.RequiredInwardDirection(current.Platform)
            : null;
        if (correction.HasValue)
        {
            bool correctionExecuted = await TryMoveAsync(
                sessionId,
                cycleId,
                StationaryPhase.MoveSecond,
                correction.Value,
                sampledAttackDurationMs,
                config,
                cancellationToken);
            if (correctionExecuted && correction.Value != initialFacing)
            {
                await RestoreInitialFacingAsync(
                    sessionId,
                    cycleId,
                    initialFacing,
                    sampledAttackDurationMs,
                    config,
                    cancellationToken);
            }
            return;
        }

        MovementDirection opposite = initialFacing == MovementDirection.Left
            ? MovementDirection.Right
            : MovementDirection.Left;
        bool firstExecuted = await TryMoveAsync(
            sessionId,
            cycleId,
            StationaryPhase.MoveFirst,
            opposite,
            sampledAttackDurationMs,
            config,
            cancellationToken);
        int gapMs = checked(
            DirectionReleaseSettleMs + random.NextInclusive(config.MoveGapMinMs, config.MoveGapMaxMs));
        await DelayPhaseAsync(
            sessionId,
            cycleId,
            StationaryPhase.MoveGap,
            gapMs,
            sampledAttackDurationMs,
            cancellationToken);
        bool secondExecuted = await TryMoveAsync(
            sessionId,
            cycleId,
            StationaryPhase.MoveSecond,
            initialFacing,
            sampledAttackDurationMs,
            config,
            cancellationToken);
        if (firstExecuted && !secondExecuted)
        {
            await RestoreInitialFacingAsync(
                sessionId,
                cycleId,
                initialFacing,
                sampledAttackDurationMs,
                config,
                cancellationToken);
        }
    }

    private async Task RunFallbackMovementCycleAsync(
        Guid sessionId,
        long cycleId,
        int sampledAttackDurationMs,
        StationaryAttackConfig config,
        CancellationToken cancellationToken)
    {
        PublishFallbackVisual(sessionId, cycleId, "VISUAL_FALLBACK_CONTINUOUS");
        MovementCycle cycle = continuousMovementPlanner.BeginCycle(config);
        MovementSegment? first = continuousMovementPlanner.TryCreateFirstSegment(config, cycle);
        MovementSegment? recovery = first is null
            ? continuousMovementPlanner.TryCreateRecoverySegment(config)
            : null;
        if (first is null)
        {
            if (recovery is null)
            {
                PublishFallbackVisual(sessionId, cycleId, "VISUAL_FALLBACK_FROZEN_NO_SAFE_MOVE");
                return;
            }
        }

        observations.BeginMovementTracking((first ?? recovery)!.Direction);
        try
        {
            if (first is null)
            {
                await ExecuteFallbackSegmentAsync(
                    sessionId,
                    cycleId,
                    StationaryPhase.MoveSecond,
                    recovery!,
                    sampledAttackDurationMs,
                    config,
                    cancellationToken);
            }
            else
            {
                await ExecuteFallbackSegmentAsync(
                    sessionId,
                    cycleId,
                    StationaryPhase.MoveFirst,
                    first,
                    sampledAttackDurationMs,
                    config,
                    cancellationToken);
                int gapMs = checked(
                    DirectionReleaseSettleMs + continuousMovementPlanner.SampleGapMs(config));
                await DelayPhaseAsync(
                    sessionId,
                    cycleId,
                    StationaryPhase.MoveGap,
                    gapMs,
                    sampledAttackDurationMs,
                    cancellationToken);
                MovementSegment second;
                try
                {
                    second = continuousMovementPlanner.CreateSecondSegment(config, cycle);
                }
                catch (InvalidOperationException exception)
                {
                    throw new VisualSessionStopException(exception.Message);
                }
                await ExecuteFallbackSegmentAsync(
                    sessionId,
                    cycleId,
                    StationaryPhase.MoveSecond,
                    second,
                    sampledAttackDurationMs,
                    config,
                    cancellationToken);
                continuousMovementPlanner.CompleteCycle(config, cycle);
            }

            int stabilizeMs = continuousMovementPlanner.SampleStabilizeMs(config);
            await DelayPhaseAsync(
                sessionId,
                cycleId,
                StationaryPhase.Stabilizing,
                stabilizeMs,
                sampledAttackDurationMs,
                cancellationToken);
            PublishFallbackVisual(sessionId, cycleId, "VISUAL_FALLBACK_CONTINUOUS");
        }
        finally
        {
            observations.EndMovementTracking();
        }
    }

    private async Task ExecuteFallbackSegmentAsync(
        Guid sessionId,
        long cycleId,
        StationaryPhase phase,
        MovementSegment segment,
        int sampledAttackDurationMs,
        StationaryAttackConfig config,
        CancellationToken cancellationToken)
    {
        InputActionResult result = await HoldAsync(
            sessionId,
            cycleId,
            phase,
            ToInputAction(segment.Direction),
            segment.HoldMs,
            sampledAttackDurationMs,
            cancellationToken);
        if (result.ActualHoldMs is not >= 1 or > StationaryAttackConfig.MovementDurationLimitMs ||
            result.ReleaseLatenessMs is null or < 0)
            throw new VisualSessionStopException("MOVEMENT_TIMING_INVALID");
        try
        {
            continuousMovementPlanner.ApplyCompletedSegment(
                segment.Direction,
                result.ActualHoldMs.Value,
                config.MaxLateralMoveMs);
        }
        catch (InvalidOperationException exception)
        {
            throw new VisualSessionStopException(exception.Message);
        }
        fallbackPlanner.TrackUnverifiedMovement(segment.Direction, result.ActualHoldMs.Value);
        relativeOffsetMs += (int)segment.Direction * result.ActualHoldMs.Value;
        PublishFallbackVisual(sessionId, cycleId, "VISUAL_FALLBACK_CONTINUOUS");
    }

    private async Task<bool> TryMoveAsync(
        Guid sessionId,
        long cycleId,
        StationaryPhase phase,
        MovementDirection requestedDirection,
        int sampledAttackDurationMs,
        StationaryAttackConfig config,
        CancellationToken cancellationToken)
    {
        VisualStationaryObservation? before = observations.Latest;
        long? authorizationRetryDeadlineMonoMs = null;
        MovementHoldResult? movement;
        MovementDirection authorizedDirection;
        bool movementTrackingActive = false;
        try
        {
            while (true)
            {
                VisualMovementAuthorization? authorization =
                    observations.TryAcquireMovementAuthorization(requestedDirection, MaximumObservationAge);
                before = authorization?.Observation ?? observations.Latest;
                if (authorization is null)
                {
                    if (before is null)
                    {
                        PublishVisual(sessionId, cycleId, null, "VISUAL_SELF_ACQUIRING");
                        return false;
                    }
                    if (!observations.IsLatestFresh(MaximumObservationAge))
                    {
                        PublishVisual(sessionId, cycleId, before, "VISUAL_OBSERVATION_STALE", "Untrusted");
                        return false;
                    }
                    PublishVisual(sessionId, cycleId, before, before.Code);
                    return false;
                }
                VisualStationaryObservation authorizedObservation = authorization.Observation;
                before = authorizedObservation;
                ObserveTrustedPosition(authorizedObservation);
                PublishVisual(sessionId, cycleId, authorizedObservation, authorizedObservation.Code);
                VisualMoveDecision decision = movementPlanner.Authorize(
                    config,
                    authorizedObservation.Platform,
                    requestedDirection);
                if (!decision.ShouldMove) return false;

                authorizedDirection = decision.Direction ?? requestedDirection;
                observations.BeginMovementTracking(authorizedDirection);
                movementTrackingActive = true;
                movement = await HoldMovementAsync(
                    sessionId,
                    cycleId,
                    phase,
                    ToInputAction(authorizedDirection),
                    decision.HoldMs,
                    sampledAttackDurationMs,
                    authorization.RevocationToken,
                    cancellationToken);
                if (movement is not null) break;

                observations.EndMovementTracking();
                movementTrackingActive = false;
                authorizationRetryDeadlineMonoMs ??= scheduler.NowMonoMs + (long)VisualFeedbackTimeout.TotalMilliseconds;
                int remainingMs = (int)Math.Max(0, authorizationRetryDeadlineMonoMs.Value - scheduler.NowMonoMs);
                if (remainingMs == 0)
                {
                    PublishVisual(sessionId, cycleId, observations.Latest, "VISUAL_AUTHORIZATION_RETRY_TIMEOUT");
                    return false;
                }
                long retryBarrier = Math.Max(
                    before.FrameSequence,
                    observations.Latest?.FrameSequence ?? before.FrameSequence);
                before = await WaitForTrustedWithSafetyChecksAsync(
                    retryBarrier,
                    TimeSpan.FromMilliseconds(remainingMs),
                    cancellationToken);
                if (before is null)
                {
                    PublishVisual(sessionId, cycleId, observations.Latest, "VISUAL_AUTHORIZATION_RETRY_TIMEOUT");
                    return false;
                }
            }

            InputActionResult result = movement.Result;
            int minimumActualHoldMs = movement.VisualAuthorityRevoked ? 0 : 1;
            if (result.ActualHoldMs is null || result.ActualHoldMs < minimumActualHoldMs ||
                result.ActualHoldMs > StationaryAttackConfig.MovementDurationLimitMs ||
                result.ReleaseLatenessMs is null or < 0)
                throw new VisualSessionStopException("MOVEMENT_TIMING_INVALID");
            relativeOffsetMs += (int)authorizedDirection * result.ActualHoldMs.Value;
            fallbackPlanner.TrackUnverifiedMovement(authorizedDirection, result.ActualHoldMs.Value);

            int stabilizeMs = random.NextInclusive(config.StabilizeMinMs, config.StabilizeMaxMs);
            await DelayPhaseAsync(
                sessionId,
                cycleId,
                StationaryPhase.Stabilizing,
                stabilizeMs,
                sampledAttackDurationMs,
                cancellationToken);
            long feedbackBarrier = Math.Max(
                before.FrameSequence,
                observations.Latest?.FrameSequence ?? before.FrameSequence);
            VisualStationaryObservation? after = await WaitForTrustedWithSafetyChecksAsync(
                feedbackBarrier,
                VisualFeedbackTimeout,
                cancellationToken);
            if (after is not null && before.Platform.CenterX.HasValue && after.Platform.CenterX.HasValue)
            {
                double jitter = Math.Abs(after.Platform.CenterX.Value - Math.Round(after.Platform.CenterX.Value));
                fallbackPlanner.RecordTrustedMovement(
                    authorizedDirection,
                    result.ActualHoldMs.Value,
                    before.Platform.CenterX.Value,
                    after.Platform.CenterX.Value);
                observations.RecordMovement(before.Platform.CenterX.Value, after.Platform.CenterX.Value, jitter);
                ObserveTrustedPosition(observations.Latest ?? after);
            }
            PublishVisual(sessionId, cycleId, after ?? observations.Latest, after?.Code ?? "VISUAL_FEEDBACK_TIMEOUT");
            return true;
        }
        finally
        {
            if (movementTrackingActive) observations.EndMovementTracking();
        }
    }

    private async Task RestoreInitialFacingAsync(
        Guid sessionId,
        long cycleId,
        MovementDirection initialFacing,
        int sampledAttackDurationMs,
        StationaryAttackConfig config,
        CancellationToken cancellationToken)
    {
        facingRestorePending = true;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SafetyCheckResult allowed = await safety.CheckAsync(cancellationToken);
                if (!allowed.Success) throw new VisualSessionStopException(allowed.Code);

                VisualStationaryObservation? current = observations.Latest;
                PublishVisual(sessionId, cycleId, current, "VISUAL_FACING_RESTORE_PENDING");
                bool unavailable = current is not { IdentityTrusted: true } ||
                    !observations.IsLatestFresh(MaximumObservationAge);
                if (unavailable &&
                    TryActivateContinuousFallback(sessionId, cycleId, initialFacing, current, config))
                {
                    facingRestorePending = false;
                    return;
                }
                if (current is { IdentityTrusted: true } && observations.IsLatestFresh(MaximumObservationAge))
                {
                    MovementDirection restoreDirection =
                        movementPlanner.RequiredInwardDirection(current.Platform) ?? initialFacing;
                    bool moved = await TryMoveAsync(
                        sessionId,
                        cycleId,
                        StationaryPhase.MoveSecond,
                        restoreDirection,
                        sampledAttackDurationMs,
                        config,
                        cancellationToken);
                    if (moved && restoreDirection == initialFacing)
                    {
                        facingRestorePending = false;
                        PublishVisual(
                            sessionId,
                            cycleId,
                            observations.Latest,
                            "VISUAL_FACING_RESTORED");
                        return;
                    }
                    current = observations.Latest;
                    if (moved) continue;
                }

                long feedbackBarrier = current?.FrameSequence ?? 0;
                await WaitForTrustedWithSafetyChecksAsync(
                    feedbackBarrier,
                    VisualFeedbackTimeout,
                    cancellationToken);
            }
        }
        finally
        {
            facingRestorePending = false;
        }
    }

    private async Task<VisualStationaryObservation?> WaitForTrustedWithSafetyChecksAsync(
        long minimumSequence,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        int remainingMs = Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds));
        while (remainingMs > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafetyCheckResult allowed = await safety.CheckAsync(cancellationToken);
            if (!allowed.Success) throw new VisualSessionStopException(allowed.Code);

            int sliceMs = Math.Min(VisualSafetyPollIntervalMs, remainingMs);
            VisualStationaryObservation? observation = await observations.WaitForTrustedAfterAsync(
                minimumSequence,
                TimeSpan.FromMilliseconds(sliceMs),
                cancellationToken);

            allowed = await safety.CheckAsync(cancellationToken);
            if (!allowed.Success) throw new VisualSessionStopException(allowed.Code);
            if (observation is not null) return observation;
            remainingMs -= sliceMs;
        }
        return null;
    }

    private async Task<MovementHoldResult?> HoldMovementAsync(
        Guid sessionId,
        long cycleId,
        StationaryPhase phase,
        StationaryInputAction action,
        int holdMs,
        int sampledAttackDurationMs,
        CancellationToken visualAuthorization,
        CancellationToken cancellationToken)
    {
        if (visualAuthorization.IsCancellationRequested) return null;
        SafetyCheckResult allowed = await safety.CheckAsync(cancellationToken);
        if (!allowed.Success) throw new VisualSessionStopException(allowed.Code);
        if (visualAuthorization.IsCancellationRequested) return null;

        PublishRhythm(sessionId, cycleId, phase, holdMs, sampledAttackDurationMs, null);
        InputActionResult down = await actions.KeyDownAsync(action, holdMs, cancellationToken);
        if (!down.Success) throw new VisualSessionStopException(down.Code);
        InputActionResult up;
        bool revoked = false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            visualAuthorization);
        try
        {
            try
            {
                await DelayWithSafetyChecksAsync(holdMs, linked.Token);
            }
            catch (OperationCanceledException) when (
                visualAuthorization.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                revoked = true;
            }
        }
        finally
        {
            up = await actions.KeyUpAsync(action, CancellationToken.None);
        }
        if (!up.Success) throw new VisualSessionStopException(up.Code);
        return new MovementHoldResult(up, revoked);
    }

    private async Task<InputActionResult> HoldAsync(
        Guid sessionId,
        long cycleId,
        StationaryPhase phase,
        StationaryInputAction action,
        int holdMs,
        int sampledAttackDurationMs,
        CancellationToken cancellationToken)
    {
        SafetyCheckResult allowed = await safety.CheckAsync(cancellationToken);
        if (!allowed.Success) throw new VisualSessionStopException(allowed.Code);
        PublishRhythm(sessionId, cycleId, phase, holdMs, sampledAttackDurationMs, null);
        InputActionResult down = await actions.KeyDownAsync(action, holdMs, cancellationToken);
        if (!down.Success) throw new VisualSessionStopException(down.Code);
        InputActionResult up;
        try
        {
            await DelayWithSafetyChecksAsync(holdMs, cancellationToken);
        }
        finally
        {
            up = await actions.KeyUpAsync(action, CancellationToken.None);
        }
        if (!up.Success) throw new VisualSessionStopException(up.Code);
        return up;
    }

    private async Task DelayPhaseAsync(
        Guid sessionId,
        long cycleId,
        StationaryPhase phase,
        int durationMs,
        int sampledAttackDurationMs,
        CancellationToken cancellationToken)
    {
        PublishRhythm(sessionId, cycleId, phase, durationMs, sampledAttackDurationMs, null);
        await DelayWithSafetyChecksAsync(durationMs, cancellationToken);
    }

    private async Task DelayWithSafetyChecksAsync(int durationMs, CancellationToken cancellationToken)
    {
        int remaining = durationMs;
        while (remaining > 0)
        {
            int slice = Math.Min(100, remaining);
            await scheduler.DelayAsync(slice, cancellationToken);
            remaining -= slice;
            if (remaining == 0) continue;
            SafetyCheckResult allowed = await safety.CheckAsync(cancellationToken);
            if (!allowed.Success) throw new VisualSessionStopException(allowed.Code);
        }
    }

    private void PublishRhythm(
        Guid sessionId,
        long cycleId,
        StationaryPhase phase,
        int durationMs,
        int sampledAttackDurationMs,
        string? reason)
    {
        long now = scheduler.NowMonoMs;
        rhythmPublisher.Publish(new StationaryRhythmState(
            StationaryAttackConfig.SchemaVersionCurrent,
            sessionId,
            cycleId,
            phase,
            sampledAttackDurationMs,
            now,
            now + durationMs,
            durationMs,
            now,
            reason,
            relativeOffsetMs));
    }

    private void PublishVisual(
        Guid sessionId,
        long cycleId,
        VisualStationaryObservation? observation,
        string code,
        string? statusOverride = null)
    {
        if (facingRestorePending)
        {
            code = "VISUAL_FACING_RESTORE_PENDING";
            statusOverride = "FacingRestorePending";
        }
        VisualPlatformState? platform = observation?.Platform;
        visualPublisher.Publish(new VisualStationaryRuntimeState(
            1,
            sessionId,
            cycleId,
            statusOverride ?? platform?.State.ToString() ?? "Acquiring",
            observation?.FrameSequence ?? 0,
            platform?.BestScore ?? 0,
            platform?.OffsetFromCenterPx,
            platform?.GuardWidthPx ?? 0,
            code,
            scheduler.NowMonoMs));
    }

    private void PublishFallbackVisual(Guid sessionId, long cycleId, string code)
    {
        VisualStationaryObservation? observation = observations.Latest;
        VisualPlatformState? platform = observation?.Platform;
        visualPublisher.Publish(new VisualStationaryRuntimeState(
            1,
            sessionId,
            cycleId,
            "FallbackContinuous",
            observation?.FrameSequence ?? 0,
            platform?.BestScore ?? 0,
            fallbackPlanner.PredictedOffsetPx.HasValue
                ? (int)Math.Round(fallbackPlanner.PredictedOffsetPx.Value)
                : null,
            platform?.GuardWidthPx ?? 0,
            code,
            scheduler.NowMonoMs));
    }

    private void ObserveTrustedPosition(VisualStationaryObservation observation)
    {
        if (observation is not { IdentityTrusted: true } ||
            !observation.Platform.OffsetFromCenterPx.HasValue)
            return;
        fallbackPlanner.ObserveTrustedPosition(
            observation.Platform.OffsetFromCenterPx.Value,
            observation.Platform.GuardWidthPx);
    }

    private static StationaryInputAction ToInputAction(MovementDirection direction) =>
        direction == MovementDirection.Left ? StationaryInputAction.MoveLeft : StationaryInputAction.MoveRight;

    private sealed class VisualSessionStopException(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }

    private sealed record MovementHoldResult(InputActionResult Result, bool VisualAuthorityRevoked);
}
