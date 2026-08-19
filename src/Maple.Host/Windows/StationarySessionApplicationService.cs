namespace Maple.Host.Windows;

public sealed class StationarySessionApplicationService(
    IWindowLocator windows,
    IForegroundSession foreground,
    IBrokerProcessLauncher broker)
{
    public async Task<SessionStartResult> PrepareAsync(
        string targetExecutablePath,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WindowIdentity> candidates =
            await windows.FindByExecutablePathAsync(targetExecutablePath, cancellationToken);
        if (candidates.Count == 0) return SessionStartResult.Failed("WINDOW_NOT_FOUND");
        if (candidates.Count != 1) return SessionStartResult.Failed("WINDOW_AMBIGUOUS");

        WindowIdentity target = candidates[0];
        ForegroundResult foregroundResult =
            await foreground.ActivateAndVerifyAsync(target, cancellationToken);
        if (!foregroundResult.Success) return SessionStartResult.Failed(foregroundResult.Code);

        Guid sessionId = Guid.NewGuid();
        BrokerLaunchResult brokerResult = await broker.StartAndArmAsync(target, sessionId, cancellationToken);
        return brokerResult.Success
            ? new SessionStartResult(true, "SESSION_PREPARED", sessionId, target, brokerResult.Connection)
            : SessionStartResult.Failed(brokerResult.Code);
    }
}
