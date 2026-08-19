using Maple.Host.Windows;

namespace Maple.Host.Safety;

public interface IBrokerLeaseProbe
{
    bool IsHealthy { get; }
}

public sealed record SafetyGateResult(bool Success, string Code)
{
    public static SafetyGateResult Allowed() => new(true, "SAFETY_ALLOWED");
    public static SafetyGateResult Rejected(string code) => new(false, code);
}

public sealed class InputSafetyCoordinator(
    WindowIdentity boundTarget,
    IWindowIdentityProbe windows,
    IBrokerLeaseProbe broker)
{
    public async Task<SafetyGateResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (!broker.IsHealthy) return SafetyGateResult.Rejected("BROKER_UNAVAILABLE");
        WindowProbeResult probe = await windows.ProbeAsync(boundTarget.Hwnd, cancellationToken);
        if (!probe.Exists || probe.Identity is null) return SafetyGateResult.Rejected("WINDOW_DISAPPEARED");
        if (probe.IsMinimized) return SafetyGateResult.Rejected("WINDOW_MINIMIZED");
        if (probe.ForegroundHwnd != boundTarget.Hwnd) return SafetyGateResult.Rejected("FOCUS_LOST");
        if (probe.Identity != boundTarget) return SafetyGateResult.Rejected("WINDOW_IDENTITY_CHANGED");
        return SafetyGateResult.Allowed();
    }
}
