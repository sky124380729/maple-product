using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Windows;
using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;
using Maple.Core.Session;
using Maple.Core.Triggers;
using Maple.Host.Broker;
using Maple.Host.Configuration;
using Maple.Host.Diagnostics;
using Maple.Host.Recognition;
using Maple.Host.Safety;
using Maple.Host.Stationary;
using Maple.Host.Windows;

namespace Maple.WindowsHost;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly JsonConfigStore configStore = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleProduct", "stationary.json"));
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim bridgeCommandGate = new(1, 1);
    private readonly string brokerPath = Path.Combine(AppContext.BaseDirectory, "Maple.InputBroker.exe");
    private readonly HotReloadConfigProvider configProvider = new(StationaryAttackConfig.Default);
    private readonly JsonLineSessionLog sessionLog = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleProduct", "sessions.jsonl"));
    private readonly LastAbnormalTerminationStore abnormalStore = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleProduct", "last-abnormal.json"));
    private readonly Diagnostics.WindowsSystemNotificationSink notificationSink = new();
    private ConfigLoadResult loadedConfig = new(StationaryAttackConfig.Default, null);
    private AbnormalTerminationRecord? lastAbnormal;
    private StationarySessionApplicationService? sessionService;
    private CancellationTokenSource? sessionCancellation;
    private IBrokerConnection? connection;
    private BrokerHeartbeatLoop? heartbeatLoop;
    private Preview.PreviewWindowHost? previewHost;
    private IWindowLocator? windowLocator;
    private WindowIdentity? boundTarget;
    private string? requestedStopReason;
    private RecognitionSession? recognitionSession;
    private IAsyncDisposable? recognitionLease;
    private readonly RecognitionBridgePublisher recognitionBridgePublisher;

    public MainWindow()
    {
        recognitionBridgePublisher = new RecognitionBridgePublisher(PublishBridgeMessage);
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            requestedStopReason = "OPERATOR_REQUESTED";
            lifetime.Cancel();
            Task.Run(() => StopStationaryAsync("OPERATOR_REQUESTED")).GetAwaiter().GetResult();
            previewHost?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            notificationSink.Dispose();
            sessionLog.Dispose();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        PlatformSupportResult platform = WindowsPlatformGuard.Check();
        if (!platform.Supported)
        {
            System.Windows.MessageBox.Show(platform.Code, "Maple Product", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }
        await ClientBrowser.EnsureCoreWebView2Async();
        loadedConfig = await configStore.LoadAsync(lifetime.Token);
        configProvider.TryUpdate(loadedConfig.Config);
        lastAbnormal = await abnormalStore.LoadAsync(lifetime.Token);
        string clientRoot = Path.Combine(AppContext.BaseDirectory, "client");
        ClientBrowser.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "maple.local",
            clientRoot,
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        ClientBrowser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        ClientBrowser.Source = new Uri("https://maple.local/index.html");
        windowLocator = new WindowsHost.Windows.NativeWindowLocator();
        sessionService = new StationarySessionApplicationService(
            windowLocator,
            new WindowsHost.Windows.NativeForegroundSession(),
            new WindowsBrokerProcessLauncher(brokerPath),
            new ManualInitialFacingProvider());
    }

    private async void OnWebMessageReceived(
        object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args)
    {
        await bridgeCommandGate.WaitAsync();
        try
        {
            using JsonDocument document = JsonDocument.Parse(args.WebMessageAsJson);
            string? command = document.RootElement.GetProperty("command").GetString();
            switch (command)
            {
                case "loadConfig": PublishLoadedConfig(); break;
                case "saveConfig": await SaveConfigAsync(document.RootElement.GetProperty("config")); break;
                case "startStationary":
                    await StartStationaryAsync(
                        document.RootElement.GetProperty("config"),
                        document.RootElement.TryGetProperty("initialFacing", out JsonElement facing)
                            ? facing.GetString()
                            : null);
                    break;
                case "stopStationary": await StopStationaryAsync("OPERATOR_REQUESTED"); break;
                case "openPreview":
                    await OpenPreviewAsync(
                        document.RootElement.TryGetProperty("recognitionEnabled", out JsonElement recognitionEnabled)
                        && recognitionEnabled.GetBoolean());
                    break;
                case "startMapRecording":
                    await OpenPreviewAsync(
                        document.RootElement.TryGetProperty("recognitionEnabled", out JsonElement recordRecognition)
                        && recordRecognition.GetBoolean(),
                        startRecording: true);
                    break;
                case "stopMapRecording":
                    if (previewHost is not null)
                        await previewHost.StopRecordingAsync("OPERATOR_STOPPED");
                    break;
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = exception.Message });
        }
        finally
        {
            bridgeCommandGate.Release();
        }
    }

    private async Task SaveConfigAsync(JsonElement element)
    {
        StationaryAttackConfig? config = element.Deserialize<StationaryAttackConfig>(JsonOptions);
        if (config is not null)
            config = config with { Source = "user", UpdatedAtUtc = DateTimeOffset.UtcNow };
        ConfigValidationResult? validation = config is null ? null : StationaryConfigValidator.Validate(config);
        if (config is null || validation is null || !validation.IsValid)
        {
            PublishConfigError(validation);
            return;
        }
        ConfigStoreResult result = await configStore.SaveAsync(config, lifetime.Token);
        if (!result.Success)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = result.Code });
            return;
        }
        ConfigProviderUpdateResult update = configProvider.TryUpdate(config);
        if (!update.Success)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = update.Code });
            return;
        }
        loadedConfig = new ConfigLoadResult(config, null);
        PublishBridgeMessage(new { type = "config.saved" });
    }

    private async Task StartStationaryAsync(JsonElement element, string? initialFacing)
    {
        StationaryAttackConfig? config = element.Deserialize<StationaryAttackConfig>(JsonOptions);
        ConfigValidationResult? validation = config is null ? null : StationaryConfigValidator.Validate(config);
        if (config is null || validation is null || !validation.IsValid)
        {
            PublishConfigError(validation);
            return;
        }
        if (sessionService is null)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = "HOST_NOT_READY" });
            return;
        }

        configProvider.TryUpdate(config);
        await StopStationaryAsync("OPERATOR_REQUESTED");
        SessionStartResult prepared = await sessionService.PrepareAsync(initialFacing, lifetime.Token);
        if (!prepared.Success || prepared.Connection is null || prepared.InitialFacing is null)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = prepared.Code });
            return;
        }

        connection = prepared.Connection;
        IBrokerConnection activeConnection = connection;
        boundTarget = prepared.Target;
        requestedStopReason = null;
        connection.SetAttackKey(config.AttackKey);
        heartbeatLoop = new BrokerHeartbeatLoop(connection);
        BrokerHeartbeatLoop activeHeartbeat = heartbeatLoop;
        heartbeatLoop.Start();
        await sessionLog.WriteAsync(
            SessionLogEntry.Create(
                prepared.SessionId,
                0,
                "Startup",
                "initialFacing",
                $"{prepared.InitialFacingSource}:{prepared.InitialFacing}"),
            lifetime.Token);
        await abnormalStore.SaveAsync(
            new AbnormalTerminationRecord(prepared.SessionId, "SESSION_IN_PROGRESS", DateTimeOffset.UtcNow),
            lifetime.Token);
        RecognitionSession? activeRecognitionSession = null;
        IAsyncDisposable? activeRecognitionLease = null;
        if (config.RecognitionEnabled)
        {
            try
            {
                activeRecognitionSession = new RecognitionSession(
                    new Preview.WindowsGraphicsCaptureSource(),
                    Preview.RecognitionProviderFactory.Create());
                activeRecognitionSession.SnapshotPublished += OnRecognitionSnapshot;
                activeRecognitionLease = await activeRecognitionSession.AcquireAsync(
                    RecognitionLeaseKind.Stationary,
                    prepared.Target!,
                    lifetime.Token);
                recognitionSession = activeRecognitionSession;
                recognitionLease = activeRecognitionLease;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                if (activeRecognitionSession is not null)
                {
                    activeRecognitionSession.SnapshotPublished -= OnRecognitionSnapshot;
                    await activeRecognitionSession.DisposeAsync();
                    activeRecognitionSession = null;
                }
                activeRecognitionLease = null;
                PublishRecognitionFault("RECOGNITION_START_FAILED");
            }
        }
        sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CancellationTokenSource activeCancellation = sessionCancellation;
        var publisher = new WebViewRhythmPublisher(
            PublishBridgeMessage,
            sessionLog,
            abnormalStore,
            new NotificationService(notificationSink),
            () => requestedStopReason);
        var safety = new BrokerStationarySafetyGate(new InputSafetyCoordinator(
            prepared.Target!,
            new WindowsHost.Windows.NativeWindowIdentityProbe(),
            connection));
        var random = new SystemRandomSource();
        var controller = new StationarySessionController(
            new LoggingActionSink(
                new ConfigAwareBrokerActionSink(connection, configProvider),
                connection,
                prepared.Target!,
                sessionLog),
            safety,
            new StopwatchMonotonicScheduler(),
            configProvider,
            new WeightedAttackDurationSampler(random),
            new StationaryMovementPlanner(random),
            new AlwaysAttackTriggerStrategy(),
            random,
            publisher);
        _ = Task.Run(async () =>
        {
            await controller.RunAsync(
                prepared.SessionId,
                prepared.InitialFacing.Value,
                null,
                activeCancellation.Token);
            await CleanupCompletedSessionAsync(
                activeConnection,
                activeHeartbeat,
                activeCancellation,
                activeRecognitionSession,
                activeRecognitionLease);
        }, activeCancellation.Token);
    }

    private async Task StopStationaryAsync(string reason)
    {
        requestedStopReason = reason;
        CancellationTokenSource? cancellationToDispose = sessionCancellation;
        sessionCancellation = null;
        IBrokerConnection? connectionToDispose = connection;
        connection = null;
        BrokerHeartbeatLoop? heartbeatToDispose = heartbeatLoop;
        heartbeatLoop = null;
        IAsyncDisposable? recognitionLeaseToDispose = recognitionLease;
        recognitionLease = null;
        RecognitionSession? recognitionSessionToDispose = recognitionSession;
        recognitionSession = null;

        cancellationToDispose?.Cancel();
        cancellationToDispose?.Dispose();
        if (connectionToDispose is not null) await connectionToDispose.ReleaseAllAsync(CancellationToken.None);
        if (recognitionLeaseToDispose is not null)
            await recognitionLeaseToDispose.DisposeAsync();
        if (recognitionSessionToDispose is not null && connectionToDispose is not null)
        {
            recognitionSessionToDispose.SnapshotPublished -= OnRecognitionSnapshot;
            await recognitionSessionToDispose.DisposeAsync();
        }
        if (heartbeatToDispose is not null) await heartbeatToDispose.DisposeAsync();
        if (connectionToDispose is not null) await connectionToDispose.DisposeAsync();
        boundTarget = null;
    }

    private void PublishBridgeMessage(object message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => PublishBridgeMessage(message));
            return;
        }
        if (ClientBrowser.CoreWebView2 is null) return;
        ClientBrowser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOptions));
    }

    private async Task CleanupCompletedSessionAsync(
        IBrokerConnection completedConnection,
        BrokerHeartbeatLoop completedHeartbeat,
        CancellationTokenSource completedCancellation,
        RecognitionSession? completedRecognitionSession,
        IAsyncDisposable? completedRecognitionLease)
    {
        if (ReferenceEquals(connection, completedConnection))
        {
            connection = null;
            boundTarget = null;
        }
        if (ReferenceEquals(heartbeatLoop, completedHeartbeat)) heartbeatLoop = null;
        if (ReferenceEquals(sessionCancellation, completedCancellation)) sessionCancellation = null;
        await completedConnection.ReleaseAllAsync(CancellationToken.None);
        if (ReferenceEquals(recognitionLease, completedRecognitionLease)) recognitionLease = null;
        if (ReferenceEquals(recognitionSession, completedRecognitionSession)) recognitionSession = null;
        if (completedRecognitionLease is not null)
        {
            await completedRecognitionLease.DisposeAsync();
        }
        if (completedRecognitionSession is not null)
        {
            completedRecognitionSession.SnapshotPublished -= OnRecognitionSnapshot;
            await completedRecognitionSession.DisposeAsync();
        }
        await completedHeartbeat.DisposeAsync();
        await completedConnection.DisposeAsync();
        completedCancellation.Dispose();
    }

    private async Task OpenPreviewAsync(bool recognitionEnabled, bool startRecording = false)
    {
        if (windowLocator is null)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = "HOST_NOT_READY" });
            return;
        }

        PreviewTargetResolution targetResolution = await PreviewTargetResolver.ResolveAsync(
            windowLocator,
            boundTarget,
            lifetime.Token);
        if (!targetResolution.Success || targetResolution.Target is null)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = targetResolution.Code });
            return;
        }

        if (previewHost is null)
        {
            previewHost = new Preview.PreviewWindowHost();
            previewHost.RecognitionSnapshotPublished += OnRecognitionSnapshot;
        }
        await previewHost.ShowAsync(targetResolution.Target.Hwnd, lifetime.Token, recognitionEnabled, startRecording);
    }

    private void OnRecognitionSnapshot(RecognitionSnapshot snapshot) =>
        recognitionBridgePublisher.TryPublish(snapshot, Environment.TickCount64);

    private void PublishRecognitionFault(string code) =>
        OnRecognitionSnapshot(RecognitionSnapshot.Create(
            Guid.NewGuid().ToString("N"), boundTarget, 0,
            Environment.TickCount64, Environment.TickCount64,
            HudObservation.Empty, [], [], [], null)
            .WithHealth(RecognitionHealth.Faulted, code));

    private void PublishLoadedConfig()
    {
        PublishBridgeMessage(new { type = "config.loaded", config = loadedConfig.Config, warning = loadedConfig.WarningCode });
        if (lastAbnormal is not null)
            PublishBridgeMessage(new { type = "stationary.abnormalTermination", record = lastAbnormal });
    }

    private void PublishConfigError(ConfigValidationResult? validation) =>
        PublishBridgeMessage(new
        {
            type = "stationary.error",
            error = "CONFIG_INVALID",
            validationErrors = validation?.Errors.Select(item => new { field = item.Field, code = item.Code }) ?? []
        });

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class WebViewRhythmPublisher(
        Action<object> post,
        ISessionLog log,
        LastAbnormalTerminationStore abnormalStore,
        NotificationService notifications,
        Func<string?> requestedStopReason) : IStationaryStatePublisher
    {
        public void Publish(StationaryRhythmState state)
        {
            string? reason = state.EarlyReleaseReason == "CANCELLED"
                ? requestedStopReason() ?? "CANCELLED"
                : state.EarlyReleaseReason;
            post(state.Phase == StationaryPhase.Stopped
                ? new { type = "stationary.stopped", state, reason }
                : new { type = "stationary.rhythm.updated", state, reason });
            _ = RecordAsync(state, reason);
        }

        private async Task RecordAsync(StationaryRhythmState state, string? reason)
        {
            string code = reason ?? "OK";
            try
            {
                await log.WriteAsync(SessionLogEntry.Create(
                    state.SessionId,
                    state.CycleId,
                    state.Phase.ToString(),
                    "phase",
                    code), CancellationToken.None);
                if (state.Phase == StationaryPhase.Stopped && NotificationPolicy.ShouldNotify(code))
                {
                    var abnormal = new AbnormalTerminationRecord(
                        state.SessionId,
                        code,
                        DateTimeOffset.UtcNow);
                    await abnormalStore.SaveAsync(abnormal, CancellationToken.None);
                    await notifications.NotifyStopAsync(state.SessionId, code, CancellationToken.None);
                }
                else if (state.Phase == StationaryPhase.Stopped)
                    await abnormalStore.ClearAsync(CancellationToken.None);
            }
            catch { }
        }
    }
}
