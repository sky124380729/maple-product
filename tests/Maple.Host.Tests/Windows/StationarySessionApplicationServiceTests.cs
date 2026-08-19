using Maple.Host.Windows;
using Maple.Core.Movement;

namespace Maple.Host.Tests.Windows;

public sealed class StationarySessionApplicationServiceTests
{
    [Fact]
    public async Task Does_not_start_broker_when_no_client_window_is_running()
    {
        var broker = new RecordingBrokerLauncher();
        var service = new StationarySessionApplicationService(
            new FakeWindowLocator([]),
            new AlwaysForeground(),
            broker,
            new ManualInitialFacingProvider());

        SessionStartResult result = await service.PrepareAsync("left", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("TARGET_NOT_FOUND", result.Code);
        Assert.Equal(0, broker.StartCalls);
    }

    [Fact]
    public async Task Does_not_guess_when_discovery_returns_multiple_client_windows()
    {
        var broker = new RecordingBrokerLauncher();
        var service = new StationarySessionApplicationService(
            new FakeWindowLocator([Window(100, 10), Window(200, 20)]),
            new AlwaysForeground(),
            broker,
            new ManualInitialFacingProvider());

        SessionStartResult result = await service.PrepareAsync("left", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("TARGET_MULTIPLE", result.Code);
        Assert.Equal(0, broker.StartCalls);
    }

    [Fact]
    public async Task Binds_identity_and_starts_broker_only_after_foreground_validation()
    {
        WindowIdentity target = Window(100, 10);
        var broker = new RecordingBrokerLauncher();
        var foreground = new AlwaysForeground();
        var service = new StationarySessionApplicationService(
            new FakeWindowLocator([target]),
            foreground,
            broker,
            new ManualInitialFacingProvider());

        SessionStartResult result = await service.PrepareAsync("right", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(target, result.Target);
        Assert.Equal(1, broker.StartCalls);
        Assert.Equal(2, foreground.Calls);
        Assert.Equal(MovementDirection.Right, result.InitialFacing);
        Assert.Equal("manual", result.InitialFacingSource);
    }

    [Fact]
    public async Task Does_not_start_session_when_post_uac_foreground_validation_fails()
    {
        var broker = new RecordingBrokerLauncher();
        var service = new StationarySessionApplicationService(
            new FakeWindowLocator([Window(100, 10)]),
            new SequencedForeground(
                ForegroundResult.Allowed(),
                ForegroundResult.Rejected("FOREGROUND_VERIFY_FAILED")),
            broker,
            new ManualInitialFacingProvider());

        SessionStartResult result = await service.PrepareAsync("left", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("FOREGROUND_VERIFY_FAILED", result.Code);
        Assert.Equal(1, broker.StartCalls);
    }

    [Fact]
    public async Task Does_not_start_broker_when_foreground_switch_fails()
    {
        var broker = new RecordingBrokerLauncher();
        var service = new StationarySessionApplicationService(
            new FakeWindowLocator([Window(100, 10)]),
            new RejectForeground(),
            broker,
            new ManualInitialFacingProvider());

        SessionStartResult result = await service.PrepareAsync("left", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("FOREGROUND_SWITCH_FAILED", result.Code);
        Assert.Equal(0, broker.StartCalls);
    }

    [Fact]
    public async Task Invalid_facing_does_not_activate_target_or_start_broker()
    {
        var broker = new RecordingBrokerLauncher();
        var foreground = new AlwaysForeground();
        var service = new StationarySessionApplicationService(
            new FakeWindowLocator([Window(100, 10)]),
            foreground,
            broker,
            new ManualInitialFacingProvider());

        SessionStartResult result = await service.PrepareAsync("up", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INITIAL_FACING_INVALID", result.Code);
        Assert.Equal(0, foreground.Calls);
        Assert.Equal(0, broker.StartCalls);
    }

    private static WindowIdentity Window(long hwnd, int pid) =>
        new(hwnd, pid, @"C:\Games\MapleStory.exe", 1_725_000_000_000);

    private sealed class FakeWindowLocator(IReadOnlyList<WindowIdentity> windows) : IWindowLocator
    {
        public Task<IReadOnlyList<WindowIdentity>> FindRunningMapleClientsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(windows);
    }

    private sealed class AlwaysForeground : IForegroundSession
    {
        public int Calls { get; private set; }

        public Task<ForegroundResult> ActivateAndVerifyAsync(WindowIdentity target, CancellationToken cancellationToken) =>
            Task.FromResult(Allow());

        private ForegroundResult Allow()
        {
            Calls++;
            return ForegroundResult.Allowed();
        }
    }

    private sealed class SequencedForeground(params ForegroundResult[] results) : IForegroundSession
    {
        private int index;

        public Task<ForegroundResult> ActivateAndVerifyAsync(WindowIdentity target, CancellationToken cancellationToken) =>
            Task.FromResult(results[index++]);
    }

    private sealed class RejectForeground : IForegroundSession
    {
        public Task<ForegroundResult> ActivateAndVerifyAsync(WindowIdentity target, CancellationToken cancellationToken) =>
            Task.FromResult(ForegroundResult.Rejected("FOREGROUND_SWITCH_FAILED"));
    }

    private sealed class RecordingBrokerLauncher : IBrokerProcessLauncher
    {
        public int StartCalls { get; private set; }

        public Task<BrokerLaunchResult> StartAndArmAsync(WindowIdentity target, Guid sessionId, CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.FromResult(BrokerLaunchResult.Started());
        }
    }
}
