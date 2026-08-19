using System.Diagnostics;
using Maple.Core.Configuration;
using Maple.Host.Broker;
using Maple.Host.Safety;

namespace Maple.Host.Stationary;

public sealed class ValidatedConfigProvider(StationaryAttackConfig config) : IStationaryConfigProvider
{
    public StationaryAttackConfig GetValidatedSnapshot() => config;
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

public sealed class BrokerHeartbeatLoop(IBrokerConnection connection) : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private Task? task;

    public void Start()
    {
        task ??= Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested && connection.IsHealthy)
            {
                await Task.Delay(500, cancellation.Token);
                await connection.HeartbeatAsync(cancellation.Token);
            }
        }, cancellation.Token);
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        if (task is not null)
        {
            try { await task; } catch (OperationCanceledException) { }
        }
        cancellation.Dispose();
    }
}
