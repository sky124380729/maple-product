using System.IO;
using Maple.Host.Broker;
using Maple.Host.Navigation;
using Maple.Host.Safety;
using Maple.Host.Stationary;
using Maple.Host.Windows;
using Maple.WindowsHost.Navigation;
using Maple.WindowsHost.Windows;
using Forms = System.Windows.Forms;

namespace Maple.WindowsHost;

public partial class MainWindow
{
    private readonly string navigationDirectoryStore = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MapleProduct",
        "navigation-directory.txt");
    private string? navigationDirectory;
    private NavigationRuntime? navigationRuntime;

    private async Task ChooseMapDirectoryAsync()
    {
        using Forms.FolderBrowserDialog dialog = new()
        {
            Description = "选择包含 .mapzip 的地图包目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = navigationDirectory ?? string.Empty
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        navigationDirectory = Path.GetFullPath(dialog.SelectedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(navigationDirectoryStore)!);
        await File.WriteAllTextAsync(navigationDirectoryStore, navigationDirectory, lifetime.Token);
        await LoadNavigationCatalogAsync();
    }

    private async Task LoadNavigationCatalogAsync()
    {
        if (navigationDirectory is null && File.Exists(navigationDirectoryStore))
        {
            string saved = (await File.ReadAllTextAsync(navigationDirectoryStore, lifetime.Token)).Trim();
            if (Directory.Exists(saved)) navigationDirectory = saved;
        }
        if (navigationDirectory is null)
        {
            PublishBridgeMessage(new { type = "navigation.catalog.loaded", directory = (string?)null, entries = Array.Empty<object>(), errors = Array.Empty<string>() });
            return;
        }
        MapCatalogResult catalog = await MapCatalog.ScanAsync(navigationDirectory, lifetime.Token);
        PublishBridgeMessage(new
        {
            type = "navigation.catalog.loaded",
            directory = navigationDirectory,
            entries = catalog.Entries.Select(entry => new
            {
                packagePath = entry.PackagePath,
                fileName = entry.FileName,
                mapName = entry.Snapshot.Name,
                canRun = entry.CanRun,
                warningCode = entry.WarningCode
            }),
            errors = catalog.Errors
        });
    }

    private async Task StartNavigationAsync(string? packagePath)
    {
        Interlocked.Increment(ref visualProfileMutationBlockCount);
        try
        {
            await StartNavigationCoreAsync(packagePath);
        }
        finally
        {
            Interlocked.Decrement(ref visualProfileMutationBlockCount);
        }
    }

    private async Task StartNavigationCoreAsync(string? packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || navigationDirectory is null || windowLocator is null)
        {
            PublishNavigationError("MAP_PACKAGE_INVALID");
            return;
        }
        if (previewHost is { IsVisualSetupActive: true } or { IsVisualProfileMutationActive: true })
        {
            PublishNavigationError("VISUAL_PROFILE_SETUP_ACTIVE");
            return;
        }
        await StopStationaryAsync("MODE_SWITCHED");
        await StopNavigationAsync("OPERATOR_REQUESTED");

        MapCatalogResult catalog = await MapCatalog.ScanAsync(navigationDirectory, lifetime.Token);
        MapCatalogEntry? entry = catalog.Entries.FirstOrDefault(item =>
            string.Equals(item.PackagePath, Path.GetFullPath(packagePath), StringComparison.OrdinalIgnoreCase));
        if (entry is null || !entry.CanRun)
        {
            PublishNavigationError(entry?.WarningCode ?? "MAP_PACKAGE_INVALID");
            return;
        }
        NavigationGraph graph;
        try { graph = new NavigationGraph(entry.Snapshot); }
        catch (NavigationGraphException exception)
        {
            PublishNavigationError(exception.Code);
            return;
        }

        NavigationSessionApplicationService service = new(
            windowLocator,
            new NativeForegroundSession(),
            new WindowsBrokerProcessLauncher(brokerPath));
        NavigationSessionStartResult prepared = await service.PrepareAsync(lifetime.Token);
        if (!prepared.Success || prepared.Target is null || prepared.Connection is not NamedPipeBrokerClient broker)
        {
            PublishNavigationError(prepared.Code);
            return;
        }
        broker.SetAttackKey(configProvider.GetValidatedSnapshot().AttackKey);
        NavigationObservationSession? observation = null;
        try
        {
            observation = new NavigationObservationSession(entry.PackagePath, entry.Sha256, entry.Snapshot);
            await observation.StartAsync(prepared.Target, lifetime.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (observation is not null) await observation.DisposeAsync();
            await broker.DisposeAsync();
            PublishNavigationError("NAVIGATION_OBSERVATION_START_FAILED:" + exception.GetType().Name);
            return;
        }

        CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        BrokerHeartbeatLoop heartbeat = new(broker);
        heartbeat.Start();
        NavigationRuntime runtime = new(cancellation, broker, heartbeat, observation, prepared.Target);
        navigationRuntime = runtime;
        boundTarget = prepared.Target;
        NavigationController controller = new(
            entry.Snapshot,
            graph,
            observation,
            broker,
            new SystemNavigationDelay(),
            new NavigationSafetyAdapter(new InputSafetyCoordinator(
                prepared.Target,
                new NativeWindowIdentityProbe(),
                broker)),
            new NavigationBridgePublisher(PublishBridgeMessage, entry.Snapshot.Name));
        PublishBridgeMessage(new { type = "navigation.started", mapName = entry.Snapshot.Name });
        runtime.RunTask = Task.Run(async () =>
        {
            try
            {
                NavigationStop stopped = await controller.RunAsync(prepared.SessionId.ToString("N"), cancellation.Token);
                PublishBridgeMessage(new { type = "navigation.stopped", reason = stopped.Code });
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                PublishNavigationError("NAVIGATION_RUNTIME_FAILED:" + exception.GetType().Name);
            }
            finally
            {
                if (ReferenceEquals(navigationRuntime, runtime)) navigationRuntime = null;
                if (ReferenceEquals(boundTarget, runtime.Target)) boundTarget = null;
                await runtime.CleanupAsync();
            }
        }, CancellationToken.None);
    }

    private async Task StopNavigationAsync(string reason)
    {
        NavigationRuntime? runtime = navigationRuntime;
        navigationRuntime = null;
        if (runtime is null) return;
        runtime.Cancellation.Cancel();
        await runtime.Connection.ReleaseAllAsync(CancellationToken.None);
        if (runtime.RunTask is not null)
            try { await runtime.RunTask; } catch (OperationCanceledException) { }
        await runtime.CleanupAsync();
        if (ReferenceEquals(boundTarget, runtime.Target)) boundTarget = null;
        PublishBridgeMessage(new { type = "navigation.stopped", reason });
    }

    private void PublishNavigationError(string code) =>
        PublishBridgeMessage(new { type = "navigation.error", error = code });

    private sealed class NavigationSafetyAdapter(InputSafetyCoordinator coordinator) : INavigationSafetyGate
    {
        public string? Evaluate()
        {
            SafetyGateResult result = coordinator.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();
            return result.Success ? null : result.Code;
        }
    }

    private sealed class NavigationBridgePublisher(Action<object> publish, string mapName) : INavigationStatePublisher
    {
        public void Publish(NavigationState state) => publish(new
        {
            type = "navigation.state.updated",
            state = new
            {
                mapName,
                phase = state.Phase,
                currentPlatformId = state.CurrentPlatformId,
                targetPlatformId = state.TargetPlatformId,
                route = state.Route,
                action = state.Action,
                faultCode = state.FaultCode,
                localizationConfidence = state.LocalizationConfidence,
                selfX = state.Self?.X,
                selfY = state.Self?.Y
            }
        });
    }

    private sealed class NavigationRuntime(
        CancellationTokenSource cancellation,
        IBrokerConnection connection,
        BrokerHeartbeatLoop heartbeat,
        NavigationObservationSession observation,
        WindowIdentity target)
    {
        private int cleaned;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public IBrokerConnection Connection { get; } = connection;
        public WindowIdentity Target { get; } = target;
        public Task? RunTask { get; set; }

        public async Task CleanupAsync()
        {
            if (Interlocked.Exchange(ref cleaned, 1) != 0) return;
            Cancellation.Cancel();
            await Connection.ReleaseAllAsync(CancellationToken.None);
            await observation.DisposeAsync();
            await heartbeat.DisposeAsync();
            await Connection.DisposeAsync();
            Cancellation.Dispose();
        }
    }
}
