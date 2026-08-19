using System.Text.Json;
using System.IO;
using System.Windows;
using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;
using Maple.Core.Session;
using Maple.Core.Triggers;
using Maple.Host.Broker;
using Maple.Host.Configuration;
using Maple.Host.Safety;
using Maple.Host.Stationary;
using Maple.Host.Windows;
using Microsoft.Win32;

namespace Maple.WindowsHost;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly JsonConfigStore configStore = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleProduct", "stationary.json"));
    private readonly CancellationTokenSource lifetime = new();
    private readonly string brokerPath = Path.Combine(AppContext.BaseDirectory, "Maple.InputBroker.exe");
    private StationarySessionApplicationService? sessionService;
    private CancellationTokenSource? sessionCancellation;
    private IBrokerConnection? connection;
    private BrokerHeartbeatLoop? heartbeatLoop;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            lifetime.Cancel();
            sessionCancellation?.Cancel();
            connection?.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
        string clientRoot = Path.Combine(AppContext.BaseDirectory, "client");
        ClientBrowser.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "maple.local",
            clientRoot,
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        ClientBrowser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        ClientBrowser.Source = new Uri("https://maple.local/index.html");
        sessionService = new StationarySessionApplicationService(
            new WindowsHost.Windows.NativeWindowLocator(),
            new WindowsHost.Windows.NativeForegroundSession(),
            new WindowsBrokerProcessLauncher(brokerPath));
    }

    private async void OnWebMessageReceived(
        object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(args.WebMessageAsJson);
            string? command = document.RootElement.GetProperty("command").GetString();
            switch (command)
            {
                case "chooseTargetExecutable": ChooseExecutable(); break;
                case "saveConfig": await SaveConfigAsync(document.RootElement.GetProperty("config")); break;
                case "startStationary": await StartStationaryAsync(document.RootElement.GetProperty("config")); break;
                case "stopStationary": await StopStationaryAsync("OPERATOR_REQUESTED"); break;
                case "openPreview": new Preview.PreviewWindowHost().Show(); break;
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = exception.Message });
        }
    }

    private void ChooseExecutable()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Windows executable (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
            PublishBridgeMessage(new { type = "targetExecutableSelected", path = Path.GetFullPath(dialog.FileName) });
    }

    private async Task SaveConfigAsync(JsonElement element)
    {
        StationaryAttackConfig? config = element.Deserialize<StationaryAttackConfig>(JsonOptions);
        if (config is null)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = "CONFIG_INVALID" });
            return;
        }
        ConfigStoreResult result = await configStore.SaveAsync(config, lifetime.Token);
        if (!result.Success) PublishBridgeMessage(new { type = "stationary.error", error = result.Code });
    }

    private async Task StartStationaryAsync(JsonElement element)
    {
        StationaryAttackConfig? config = element.Deserialize<StationaryAttackConfig>(JsonOptions);
        if (config is null || !StationaryConfigValidator.Validate(config).IsValid)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = "CONFIG_INVALID" });
            return;
        }
        if (sessionService is null)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = "HOST_NOT_READY" });
            return;
        }

        await StopStationaryAsync("OPERATOR_REQUESTED");
        SessionStartResult prepared = await sessionService.PrepareAsync(config.TargetExecutablePath, lifetime.Token);
        if (!prepared.Success || prepared.Connection is null)
        {
            PublishBridgeMessage(new { type = "stationary.error", error = prepared.Code });
            return;
        }

        connection = prepared.Connection;
        connection.SetAttackKey(config.AttackKey);
        heartbeatLoop = new BrokerHeartbeatLoop(connection);
        heartbeatLoop.Start();
        sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var publisher = new WebViewRhythmPublisher(PublishBridgeMessage);
        var safety = new BrokerStationarySafetyGate(new InputSafetyCoordinator(
            prepared.Target!,
            new WindowsHost.Windows.NativeWindowIdentityProbe(),
            connection));
        var random = new SystemRandomSource();
        var controller = new StationarySessionController(
            connection,
            safety,
            new StopwatchMonotonicScheduler(),
            new ValidatedConfigProvider(config),
            new WeightedAttackDurationSampler(random),
            new StationaryMovementPlanner(random),
            new AlwaysAttackTriggerStrategy(),
            random,
            publisher);
        _ = Task.Run(async () =>
        {
            await controller.RunAsync(prepared.SessionId, null, sessionCancellation.Token);
            PublishBridgeMessage(new { type = "stationary.stopped", reason = "SESSION_ENDED" });
        }, sessionCancellation.Token);
    }

    private async Task StopStationaryAsync(string reason)
    {
        sessionCancellation?.Cancel();
        sessionCancellation?.Dispose();
        sessionCancellation = null;
        if (connection is not null)
        {
            await connection.ReleaseAllAsync(CancellationToken.None);
            if (heartbeatLoop is not null) await heartbeatLoop.DisposeAsync();
            heartbeatLoop = null;
            await connection.DisposeAsync();
            connection = null;
        }
        if (reason != "OPERATOR_REQUESTED") PublishBridgeMessage(new { type = "stationary.stopped", reason });
    }

    private void PublishBridgeMessage(object message)
    {
        if (ClientBrowser.CoreWebView2 is null) return;
        ClientBrowser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOptions));
    }

    private sealed class WebViewRhythmPublisher(Action<object> post) : IStationaryStatePublisher
    {
        public void Publish(StationaryRhythmState state) => post(new { type = "stationary.rhythm.updated", state });
    }
}
