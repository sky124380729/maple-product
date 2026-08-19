using Maple.Host.Windows;

namespace Maple.Host.Tests.Windows;

public sealed class StationarySessionApplicationServiceTests
{
    [Fact]
    public async Task Does_not_start_broker_when_no_window_matches_the_configured_path()
    {
        var broker = new RecordingBrokerLauncher();
        var service = new StationarySessionApplicationService(
            new FakeWindowLocator([]),
            new AlwaysForeground(),
            broker);

        SessionStartResult result = await service.PrepareAsync(@"C:\Games\MapleStory.exe", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WINDOW_NOT_FOUND", result.Code);
        Assert.Equal(0, broker.StartCalls);
    }

    [Fact]
    public async Task Does_not_guess_when_multiple_windows_match_the_same_path()
    {
        var broker = new RecordingBrokerLauncher();
        var service = new StationarySessionApplicationService(
            new FakeWindowLocator([Window(100, 10), Window(200, 20)]),
            new AlwaysForeground(),
            broker);

        SessionStartResult result = await service.PrepareAsync(@"C:\Games\MapleStory.exe", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WINDOW_AMBIGUOUS", result.Code);
        Assert.Equal(0, broker.StartCalls);
    }

    [Fact]
    public async Task Binds_identity_and_starts_broker_only_after_foreground_validation()
    {
        WindowIdentity target = Window(100, 10);
        var broker = new RecordingBrokerLauncher();
        var service = new StationarySessionApplicationService(
            new FakeWindowLocator([target]),
            new AlwaysForeground(),
            broker);

        SessionStartResult result = await service.PrepareAsync(@"C:\Games\MapleStory.exe", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(target, result.Target);
        Assert.Equal(1, broker.StartCalls);
    }

    [Fact]
    public async Task Does_not_start_broker_when_foreground_switch_fails()
    {
        var broker = new RecordingBrokerLauncher();
        var service = new StationarySessionApplicationService(
            new FakeWindowLocator([Window(100, 10)]),
            new RejectForeground(),
            broker);

        SessionStartResult result = await service.PrepareAsync(@"C:\Games\MapleStory.exe", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("FOREGROUND_SWITCH_FAILED", result.Code);
        Assert.Equal(0, broker.StartCalls);
    }

    private static WindowIdentity Window(long hwnd, int pid) =>
        new(hwnd, pid, @"C:\Games\MapleStory.exe", 1_725_000_000_000);

    private sealed class FakeWindowLocator(IReadOnlyList<WindowIdentity> windows) : IWindowLocator
    {
        public Task<IReadOnlyList<WindowIdentity>> FindByExecutablePathAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult(windows);
    }

    private sealed class AlwaysForeground : IForegroundSession
    {
        public Task<ForegroundResult> ActivateAndVerifyAsync(WindowIdentity target, CancellationToken cancellationToken) =>
            Task.FromResult(ForegroundResult.Allowed());
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
