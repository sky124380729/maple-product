using Maple.Host.Broker;
using Maple.Host.Safety;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class BrokerHeartbeatLoopTests
{
    [Fact]
    public async Task Dispose_waits_for_in_flight_heartbeat_and_stops_the_loop()
    {
        var connection = new BlockingHeartbeatConnection();
        var loop = new BrokerHeartbeatLoop(connection);
        loop.Start();
        await connection.HeartbeatStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task dispose = loop.DisposeAsync().AsTask();

        Assert.False(dispose.IsCompleted);
        connection.CompleteHeartbeat();
        await dispose.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, connection.HeartbeatCount);
    }

    [Fact]
    public async Task Failed_heartbeat_marks_connection_unhealthy_and_releases_keys()
    {
        var connection = new FailingHeartbeatConnection();
        var loop = new BrokerHeartbeatLoop(connection);
        loop.Start();

        await connection.HeartbeatCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await loop.DisposeAsync();

        Assert.False(connection.IsHealthy);
        Assert.Equal(1, connection.ReleaseCount);
    }

    private sealed class BlockingHeartbeatConnection : IBrokerConnection
    {
        private readonly TaskCompletionSource heartbeatCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource HeartbeatStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int HeartbeatCount { get; private set; }
        public Guid SessionId { get; } = Guid.NewGuid();
        public bool IsHealthy => true;

        public async Task<InputActionResult> HeartbeatAsync(CancellationToken cancellationToken)
        {
            HeartbeatCount++;
            HeartbeatStarted.TrySetResult();
            await heartbeatCompletion.Task;
            return InputActionResult.Ok("HEARTBEAT_OK");
        }

        public void CompleteHeartbeat() => heartbeatCompletion.TrySetResult();
        public void SetAttackKey(string key) { }
        public Task<InputActionResult> KeyDownAsync(StationaryInputAction action, int leaseMs, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<InputActionResult> KeyUpAsync(StationaryInputAction action, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<InputActionResult> ReleaseAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingHeartbeatConnection : IBrokerConnection
    {
        public TaskCompletionSource HeartbeatCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid SessionId { get; } = Guid.NewGuid();
        public bool IsHealthy { get; private set; } = true;
        public int ReleaseCount { get; private set; }
        public Task<InputActionResult> HeartbeatAsync(CancellationToken cancellationToken)
        {
            HeartbeatCompleted.TrySetResult();
            return Task.FromResult(InputActionResult.Fail("HEARTBEAT_REJECTED"));
        }
        public void MarkUnhealthy() => IsHealthy = false;
        public Task<InputActionResult> ReleaseAllAsync(CancellationToken cancellationToken)
        {
            ReleaseCount++;
            return Task.FromResult(InputActionResult.Ok("ALL_KEYS_RELEASED"));
        }
        public void SetAttackKey(string key) { }
        public Task<InputActionResult> KeyDownAsync(StationaryInputAction action, int leaseMs, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InputActionResult> KeyUpAsync(StationaryInputAction action, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
