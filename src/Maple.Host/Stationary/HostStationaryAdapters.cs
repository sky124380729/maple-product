using System.Diagnostics;
using Maple.Core.Configuration;
using Maple.Host.Broker;
using Maple.Host.Safety;

namespace Maple.Host.Stationary;

public sealed record ConfigProviderUpdateResult(bool Success, string Code)
{
    public static ConfigProviderUpdateResult Updated() => new(true, "CONFIG_UPDATED");
    public static ConfigProviderUpdateResult Rejected() => new(false, "CONFIG_INVALID");
}

public sealed class HotReloadConfigProvider : IStationaryConfigProvider
{
    private StationaryAttackConfig snapshot;

    public HotReloadConfigProvider(StationaryAttackConfig initialConfig)
    {
        snapshot = CopyValidated(initialConfig);
    }

    public StationaryAttackConfig GetValidatedSnapshot() => Copy(Volatile.Read(ref snapshot));

    public ConfigProviderUpdateResult TryUpdate(StationaryAttackConfig config)
    {
        if (!StationaryConfigValidator.Validate(config).IsValid)
            return ConfigProviderUpdateResult.Rejected();

        Volatile.Write(ref snapshot, Copy(config));
        return ConfigProviderUpdateResult.Updated();
    }

    private static StationaryAttackConfig CopyValidated(StationaryAttackConfig config)
    {
        if (!StationaryConfigValidator.Validate(config).IsValid)
            throw new ArgumentException("Initial configuration must be valid.", nameof(config));
        return Copy(config);
    }

    private static StationaryAttackConfig Copy(StationaryAttackConfig config) =>
        config with { AttackBands = config.AttackBands.ToArray() };
}

public sealed class StopwatchMonotonicScheduler : IMonotonicScheduler
{
    private readonly long started = Stopwatch.GetTimestamp();
    public long NowMonoMs => (long)((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency);
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
        Task.Delay(milliseconds, cancellationToken);
}

public sealed class BrokerStationarySafetyGate(InputSafetyCoordinator coordinator) : IStationarySafetyGate
{
    public async Task<SafetyCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        SafetyGateResult result = await coordinator.CheckAsync(cancellationToken);
        return result.Success ? SafetyCheckResult.Allowed() : SafetyCheckResult.Rejected(result.Code);
    }
}

public sealed class ConfigAwareBrokerActionSink(
    IBrokerConnection broker,
    IStationaryConfigProvider configs) : IStationaryActionSink
{
    public Task<InputActionResult> KeyDownAsync(
        StationaryInputAction action,
        int leaseMs,
        CancellationToken cancellationToken)
    {
        if (action == StationaryInputAction.Attack)
            broker.SetAttackKey(configs.GetValidatedSnapshot().AttackKey);
        return broker.KeyDownAsync(action, leaseMs, cancellationToken);
    }

    public Task<InputActionResult> KeyUpAsync(
        StationaryInputAction action,
        CancellationToken cancellationToken) =>
        broker.KeyUpAsync(action, cancellationToken);

    public Task<InputActionResult> ReleaseAllAsync(CancellationToken cancellationToken) =>
        broker.ReleaseAllAsync(cancellationToken);
}

public sealed class BrokerHeartbeatLoop(IBrokerConnection connection) : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private Task? task;
    private int disposed;

    public void Start()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        task ??= Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested && connection.IsHealthy)
            {
                await Task.Delay(500, cancellation.Token);
                try
                {
                    InputActionResult heartbeat = await connection.HeartbeatAsync(cancellation.Token);
                    if (heartbeat.Success) continue;
                    connection.MarkUnhealthy();
                    await connection.ReleaseAllAsync(CancellationToken.None);
                    break;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    connection.MarkUnhealthy();
                    try { await connection.ReleaseAllAsync(CancellationToken.None); } catch { }
                    break;
                }
            }
        }, cancellation.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        cancellation.Cancel();
        if (task is not null)
        {
            try { await task; } catch (OperationCanceledException) { }
        }
        cancellation.Dispose();
    }
}
