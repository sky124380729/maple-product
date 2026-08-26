using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;
using Maple.Host.Broker;
using Maple.Host.Diagnostics;
using Maple.Host.Safety;
using Maple.Host.Stationary;
using Maple.Host.Windows;

namespace Maple.WindowsHost;

public partial class MainWindow
{
    private VisualStationaryObservationSession? visualObservation;

    private async Task StartVisualStationaryAsync(
        StationaryAttackConfig config,
        string? initialFacing)
    {
        Interlocked.Increment(ref visualProfileMutationBlockCount);
        try
        {
            await StartVisualStationaryCoreAsync(config, initialFacing);
        }
        finally
        {
            Interlocked.Decrement(ref visualProfileMutationBlockCount);
        }
    }

    private async Task StartVisualStationaryCoreAsync(
        StationaryAttackConfig config,
        string? initialFacing)
    {
        if (windowLocator is null || sessionService is null)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = "HOST_NOT_READY" });
            return;
        }

        configProvider.TryUpdate(config);
        await StopStationaryAsync("OPERATOR_REQUESTED");
        PreviewTargetResolution targetResolution = await PreviewTargetResolver.ResolveAsync(
            windowLocator,
            boundTarget,
            lifetime.Token);
        if (!targetResolution.Success || targetResolution.Target is null)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = targetResolution.Code });
            return;
        }

        Preview.PreviewWindowHost activePreview = EnsurePreviewHost();
        await activePreview.ShowAsync(targetResolution.Target.Hwnd, lifetime.Token, recognitionEnabled: false);
        if (activePreview.IsVisualSetupActive || activePreview.IsVisualProfileMutationActive)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = "VISUAL_PROFILE_SETUP_ACTIVE" });
            return;
        }
        VisualStationaryProfile? profile = await WaitForVisualProfileAsync(activePreview, lifetime.Token);
        if (profile is null)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = "VISUAL_PROFILE_NOT_CONFIGURED" });
            activePreview.BeginVisualSetup();
            return;
        }

        VisualStationaryObservationSession observation = activePreview.ResetVisualObservation(profile);
        VisualStationaryObservation? preflight = await observation.WaitForTrustedAfterAsync(
            0,
            TimeSpan.FromSeconds(3),
            lifetime.Token);
        VisualStationaryObservation? startupObservation = preflight ?? observation.Latest;
        VisualStartupDecision startupDecision = VisualStationaryStartupPolicy.Decide(startupObservation);
        if (!startupDecision.ShouldStart)
        {
            await PublishVisualStartupFailureAsync(Guid.Empty, observation, startupObservation);
            return;
        }

        SessionStartResult prepared = await sessionService.PrepareAsync(initialFacing, lifetime.Token);
        if (!prepared.Success || prepared.Connection is null || prepared.InitialFacing is null)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = prepared.Code });
            return;
        }
        IBrokerConnection preparedConnection = prepared.Connection;
        preparedConnection.SetAttackKey(config.AttackKey);
        var preparedHeartbeat = new BrokerHeartbeatLoop(preparedConnection);
        preparedHeartbeat.Start();
        VisualStationaryObservation? beforeInputObservation = observation.Latest;
        VisualStartupDecision beforeInputDecision = VisualStationaryStartupPolicy.DecideBeforeInput(
            beforeInputObservation,
            observation.IsLatestFresh(TimeSpan.FromSeconds(1)));
        if (!beforeInputDecision.ShouldStart)
        {
            await preparedConnection.ReleaseAllAsync(CancellationToken.None);
            await preparedHeartbeat.DisposeAsync();
            await preparedConnection.DisposeAsync();
            await PublishVisualStartupFailureAsync(
                prepared.SessionId,
                observation,
                beforeInputObservation,
                beforeInputDecision.Code);
            return;
        }
        connection = preparedConnection;
        IBrokerConnection activeConnection = connection;
        boundTarget = prepared.Target;
        requestedStopReason = null;
        heartbeatLoop = preparedHeartbeat;
        BrokerHeartbeatLoop activeHeartbeat = heartbeatLoop;
        visualObservation = observation;
        await sessionLog.WriteAsync(
            SessionLogEntry.Create(
                prepared.SessionId,
                0,
                "VisualStartup",
                startupDecision.ShouldStart && startupObservation is { IdentityTrusted: false }
                    ? "profile-untrusted-frozen"
                    : "profile",
                $"{profile.FrameWidth}x{profile.FrameHeight}:{profile.Platform.X},{profile.Platform.Right};{startupDecision.Code}"),
            lifetime.Token);
        await abnormalStore.SaveAsync(
            new AbnormalTerminationRecord(prepared.SessionId, "SESSION_IN_PROGRESS", DateTimeOffset.UtcNow),
            lifetime.Token);

        sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CancellationTokenSource activeCancellation = sessionCancellation;
        var rhythmPublisher = new WebViewRhythmPublisher(
            PublishBridgeMessage,
            sessionLog,
            abnormalStore,
            new NotificationService(notificationSink),
            () => requestedStopReason);
        var visualPublisher = new WebViewVisualStatePublisher(
            prepared.SessionId,
            PublishBridgeMessage,
            sessionLog,
            profile.IdentityKind);
        observation.ObservationPublished += visualPublisher.PublishObservation;
        var safety = new BrokerStationarySafetyGate(new InputSafetyCoordinator(
            prepared.Target!,
            new WindowsHost.Windows.NativeWindowIdentityProbe(),
            connection));
        var random = new SystemRandomSource();
        var controller = new VisualStationarySessionController(
            new LoggingActionSink(
                new ConfigAwareBrokerActionSink(connection, configProvider),
                connection,
                prepared.Target!,
                sessionLog),
            safety,
            new StopwatchMonotonicScheduler(),
            configProvider,
            new WeightedAttackDurationSampler(random),
            new VisualStationaryMovementPlanner(random),
            new VisualFallbackMovementPlanner(random, profile.Platform.Width),
            observation,
            random,
            rhythmPublisher,
            visualPublisher,
            new SessionLogVisualFallbackTelemetrySink(sessionLog));
        Task runTask = Task.Run(async () =>
        {
            try
            {
                await controller.RunAsync(
                    prepared.SessionId,
                    prepared.InitialFacing.Value,
                    null,
                    activeCancellation.Token);
            }
            finally
            {
                observation.ObservationPublished -= visualPublisher.PublishObservation;
                if (ReferenceEquals(visualObservation, observation)) visualObservation = null;
                await CleanupCompletedSessionAsync(
                    activeConnection,
                    activeHeartbeat,
                    activeCancellation,
                    null,
                    null);
            }
        });
        stationarySessionRun = new StationarySessionRun(
            activeCancellation,
            runTask,
            () => AbortVisualRunAsync(
                activeConnection,
                activeHeartbeat,
                activeCancellation,
                observation));
    }

    private static async Task<VisualStationaryProfile?> WaitForVisualProfileAsync(
        Preview.PreviewWindowHost preview,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (preview.CurrentVisualProfile is { } profile) return profile;
            await Task.Delay(50, cancellationToken);
        }
        return null;
    }

    private async Task PublishVisualStartupFailureAsync(
        Guid sessionId,
        VisualStationaryObservationSession observation,
        VisualStationaryObservation? result,
        string? codeOverride = null)
    {
        VisualStationaryObservation? diagnostic = result ?? observation.Latest;
        string code = codeOverride ?? diagnostic?.Code ?? "VISUAL_SELF_NOT_TRUSTED";
        try
        {
            await sessionLog.WriteAsync(
                SessionLogEntry.Create(
                    sessionId,
                    0,
                    "VisualStartup",
                    "identity-rejected",
                    $"{code};score={diagnostic?.Platform.BestScore ?? 0:F3};sequence={diagnostic?.FrameSequence ?? 0}"),
                CancellationToken.None);
        }
        catch
        {
        }
        PublishBridgeMessage(new { type = "stationary.error", error = code });
    }

    private Task AbortVisualRunAsync(
        IBrokerConnection activeConnection,
        BrokerHeartbeatLoop activeHeartbeat,
        CancellationTokenSource activeCancellation,
        VisualStationaryObservationSession observation)
    {
        if (ReferenceEquals(visualObservation, observation)) visualObservation = null;
        return AbortStationaryRunAsync(
            activeConnection,
            activeHeartbeat,
            activeCancellation,
            null,
            null);
    }

    private sealed class WebViewVisualStatePublisher(
        Guid sessionId,
        Action<object> post,
        ISessionLog log,
        VisualIdentityKind identityKind) : IVisualStationaryStatePublisher
    {
        private readonly object observationSync = new();
        private long lastObservationPublishedAt;
        private string? lastObservationStatus;
        private string? lastObservationCode;
        private bool facingRestorePending;
        private bool fallbackActive;
        private int? fallbackOffsetPx;
        private int fallbackGuardWidthPx;

        public void Publish(VisualStationaryRuntimeState state)
        {
            state = state with { IdentityKind = identityKind.ToString() };
            lock (observationSync)
            {
                facingRestorePending = state.Status == "FacingRestorePending";
                fallbackActive = state.Status == "FallbackContinuous";
                if (fallbackActive)
                {
                    fallbackOffsetPx = state.VisualOffsetPx;
                    fallbackGuardWidthPx = state.GuardWidthPx;
                }
                else
                {
                    fallbackOffsetPx = null;
                    fallbackGuardWidthPx = 0;
                }
                post(new { type = "visualStationary.state.updated", state });
            }
            _ = WriteAsync(state);
        }

        public void PublishObservation(VisualStationaryObservation observation)
        {
            long now = Environment.TickCount64;
            string status;
            string code;
            lock (observationSync)
            {
                status = facingRestorePending
                    ? "FacingRestorePending"
                    : fallbackActive
                        ? "FallbackContinuous"
                        : observation.Platform.State.ToString();
                code = facingRestorePending
                    ? "VISUAL_FACING_RESTORE_PENDING"
                    : fallbackActive
                        ? "VISUAL_FALLBACK_CONTINUOUS"
                        : observation.Code;
                bool authorityChanged = status != lastObservationStatus ||
                    code != lastObservationCode;
                if (!authorityChanged && now - lastObservationPublishedAt < 200) return;
                lastObservationPublishedAt = now;
                lastObservationStatus = status;
                lastObservationCode = code;
                post(new
                {
                    type = "visualStationary.state.updated",
                    state = new VisualStationaryRuntimeState(
                        1,
                        sessionId,
                        0,
                        status,
                        observation.FrameSequence,
                        observation.Platform.BestScore,
                        fallbackActive ? fallbackOffsetPx : observation.Platform.OffsetFromCenterPx,
                        fallbackActive ? fallbackGuardWidthPx : observation.Platform.GuardWidthPx,
                        code,
                        now,
                        identityKind.ToString())
                });
            }
        }

        private async Task WriteAsync(VisualStationaryRuntimeState state)
        {
            try
            {
                await log.WriteAsync(
                    SessionLogEntry.Create(
                        state.SessionId,
                        state.CycleId,
                        "VisualSafety",
                        state.Status,
                        $"{state.Code};score={state.BestScore:F3};offsetPx={state.VisualOffsetPx};guardPx={state.GuardWidthPx}"),
                    CancellationToken.None);
            }
            catch
            {
            }
        }
    }
}
