using Maple.Host.Safety;
using Maple.Host.Windows;

namespace Maple.Host.Tests.Safety;

public sealed class InputSafetyCoordinatorTests
{
    [Fact]
    public async Task Rejects_when_foreground_identity_changes()
    {
        WindowIdentity bound = new(100, 10, @"C:\Games\MapleStory.exe", 1234);
        var gate = new InputSafetyCoordinator(
            bound,
            new FakeIdentityProbe(new WindowIdentity(100, 11, bound.ProcessPath, bound.ProcessStartedAtUnixMs)),
            new HealthyBrokerLease());

        SafetyGateResult result = await gate.CheckAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WINDOW_IDENTITY_CHANGED", result.Code);
    }

    [Fact]
    public async Task Rejects_when_target_is_not_the_foreground_window()
    {
        WindowIdentity bound = new(100, 10, @"C:\Games\MapleStory.exe", 1234);
        var gate = new InputSafetyCoordinator(bound, new FakeIdentityProbe(bound, foregroundHwnd: 200), new HealthyBrokerLease());

        SafetyGateResult result = await gate.CheckAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("FOCUS_LOST", result.Code);
    }

    private sealed class FakeIdentityProbe(WindowIdentity current, long? foregroundHwnd = null) : IWindowIdentityProbe
    {
        public Task<WindowProbeResult> ProbeAsync(long hwnd, CancellationToken cancellationToken) =>
            Task.FromResult(new WindowProbeResult(current, foregroundHwnd ?? current.Hwnd, IsMinimized: false, Exists: true));
    }

    private sealed class HealthyBrokerLease : IBrokerLeaseProbe
    {
        public bool IsHealthy => true;
    }
}
