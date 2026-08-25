using Maple.Host.Broker;

namespace Maple.Host.Windows;

public sealed record NavigationSessionStartResult(
    bool Success,
    string Code,
    Guid SessionId,
    WindowIdentity? Target,
    IBrokerConnection? Connection)
{
    public static NavigationSessionStartResult Failed(string code) => new(false, code, Guid.Empty, null, null);
}

public sealed class NavigationSessionApplicationService(
    IWindowLocator windows,
    IForegroundSession foreground,
    IBrokerProcessLauncher broker)
{
    public async Task<NavigationSessionStartResult> PrepareAsync(CancellationToken token)
    {
        IReadOnlyList<WindowIdentity> candidates = await windows.FindRunningMapleClientsAsync(token);
        if (candidates.Count == 0) return NavigationSessionStartResult.Failed("TARGET_NOT_FOUND");
        if (candidates.Count != 1) return NavigationSessionStartResult.Failed("TARGET_MULTIPLE");
        WindowIdentity target = candidates[0];
        ForegroundResult before = await foreground.ActivateAndVerifyAsync(target, token);
        if (!before.Success) return NavigationSessionStartResult.Failed(before.Code);
        Guid sessionId = Guid.NewGuid();
        BrokerLaunchResult launched = await broker.StartAndArmAsync(target, sessionId, token);
        if (!launched.Success) return NavigationSessionStartResult.Failed(launched.Code);
        ForegroundResult after = await foreground.ActivateAndVerifyAsync(target, token);
        if (!after.Success)
        {
            if (launched.Connection is not null) await launched.Connection.DisposeAsync();
            return NavigationSessionStartResult.Failed(after.Code);
        }
        return new NavigationSessionStartResult(true, "NAVIGATION_PREPARED", sessionId, target, launched.Connection);
    }
}
