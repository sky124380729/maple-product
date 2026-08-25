using Maple.Host.Windows;

namespace Maple.Host.Tests.Windows;

public sealed class NavigationSessionApplicationServiceTests
{
    [Fact]
    public async Task Prepares_single_client_without_facing_selection()
    {
        WindowIdentity target = new(100, 10, @"C:\Games\MapleStory.exe", 1234);
        RecordingBroker broker = new();
        CountingForeground foreground = new();
        NavigationSessionApplicationService service = new(new Locator([target]), foreground, broker);

        NavigationSessionStartResult result = await service.PrepareAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(target, result.Target);
        Assert.Equal(1, broker.Calls);
        Assert.Equal(2, foreground.Calls);
    }

    private sealed class Locator(IReadOnlyList<WindowIdentity> values) : IWindowLocator
    { public Task<IReadOnlyList<WindowIdentity>> FindRunningMapleClientsAsync(CancellationToken token) => Task.FromResult(values); }
    private sealed class CountingForeground : IForegroundSession
    {
        public int Calls { get; private set; }
        public Task<ForegroundResult> ActivateAndVerifyAsync(WindowIdentity target, CancellationToken token)
        { Calls++; return Task.FromResult(ForegroundResult.Allowed()); }
    }
    private sealed class RecordingBroker : IBrokerProcessLauncher
    {
        public int Calls { get; private set; }
        public Task<BrokerLaunchResult> StartAndArmAsync(WindowIdentity target, Guid id, CancellationToken token)
        { Calls++; return Task.FromResult(BrokerLaunchResult.Started()); }
    }
}
