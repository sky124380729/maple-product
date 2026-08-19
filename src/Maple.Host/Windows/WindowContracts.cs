namespace Maple.Host.Windows;

using Maple.Host.Broker;
using Maple.Core.Movement;

public sealed record WindowIdentity(
    long Hwnd,
    int ProcessId,
    string ProcessPath,
    long ProcessStartedAtUnixMs);

public sealed record WindowProbeResult(
    WindowIdentity? Identity,
    long ForegroundHwnd,
    bool IsMinimized,
    bool Exists);

public interface IWindowLocator
{
    Task<IReadOnlyList<WindowIdentity>> FindRunningMapleClientsAsync(CancellationToken cancellationToken);
}

public sealed record PreviewTargetResolution(bool Success, string Code, WindowIdentity? Target)
{
    public static PreviewTargetResolution Resolved(WindowIdentity target) =>
        new(true, "PREVIEW_TARGET_RESOLVED", target);

    public static PreviewTargetResolution Rejected(string code) =>
        new(false, code, null);
}

public static class PreviewTargetResolver
{
    public static async Task<PreviewTargetResolution> ResolveAsync(
        IWindowLocator windows,
        WindowIdentity? boundTarget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (boundTarget is not null) return PreviewTargetResolution.Resolved(boundTarget);

        IReadOnlyList<WindowIdentity> candidates =
            await windows.FindRunningMapleClientsAsync(cancellationToken);
        return candidates.Count switch
        {
            0 => PreviewTargetResolution.Rejected("TARGET_NOT_FOUND"),
            1 => PreviewTargetResolution.Resolved(candidates[0]),
            _ => PreviewTargetResolution.Rejected("TARGET_MULTIPLE")
        };
    }
}

public static class MapleClientWindowFingerprint
{
    public const string Title = "冒险岛怀旧服";
    public const string ClassName = "UnityWndClass";

    public static bool Matches(bool visible, string title, string className) =>
        visible &&
        string.Equals(title, Title, StringComparison.Ordinal) &&
        string.Equals(className, ClassName, StringComparison.Ordinal);
}

public interface IForegroundSession
{
    Task<ForegroundResult> ActivateAndVerifyAsync(WindowIdentity target, CancellationToken cancellationToken);
}

public sealed record ForegroundResult(bool Success, string Code)
{
    public static ForegroundResult Allowed() => new(true, "FOREGROUND_VERIFIED");
    public static ForegroundResult Rejected(string code) => new(false, code);
}

public interface IWindowIdentityProbe
{
    Task<WindowProbeResult> ProbeAsync(long hwnd, CancellationToken cancellationToken);
}

public interface IBrokerProcessLauncher
{
    Task<BrokerLaunchResult> StartAndArmAsync(
        WindowIdentity target,
        Guid sessionId,
        CancellationToken cancellationToken);
}

public sealed record BrokerLaunchResult(bool Success, string Code, IBrokerConnection? Connection = null)
{
    public static BrokerLaunchResult Started(IBrokerConnection? connection = null) => new(true, "BROKER_ARMED", connection);
    public static BrokerLaunchResult Failed(string code) => new(false, code);
}

public sealed record SessionStartResult(
    bool Success,
    string Code,
    Guid SessionId,
    WindowIdentity? Target,
    IBrokerConnection? Connection,
    MovementDirection? InitialFacing,
    string? InitialFacingSource)
{
    public static SessionStartResult Failed(string code) => new(false, code, Guid.Empty, null, null, null, null);
    public static SessionStartResult Started(
        Guid sessionId,
        WindowIdentity target,
        MovementDirection initialFacing,
        string initialFacingSource) =>
        new(true, "SESSION_PREPARED", sessionId, target, null, initialFacing, initialFacingSource);
}
