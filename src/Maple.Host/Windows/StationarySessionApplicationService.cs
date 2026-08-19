namespace Maple.Host.Windows;

public sealed class StationarySessionApplicationService(
    IWindowLocator windows,
    IForegroundSession foreground,
    IBrokerProcessLauncher broker,
    IInitialFacingProvider facing)
{
    public async Task<SessionStartResult> PrepareAsync(
        string? initialFacingSelection,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WindowIdentity> candidates =
            await windows.FindRunningMapleClientsAsync(cancellationToken);
        if (candidates.Count == 0) return SessionStartResult.Failed("TARGET_NOT_FOUND");
        if (candidates.Count != 1) return SessionStartResult.Failed("TARGET_MULTIPLE");

        WindowIdentity target = candidates[0];
        InitialFacingResolution facingResult = await facing.ResolveAsync(
            target,
            initialFacingSelection,
            cancellationToken);
        if (!facingResult.Success || facingResult.Direction is null)
            return SessionStartResult.Failed(facingResult.Code);

        ForegroundResult foregroundResult =
            await foreground.ActivateAndVerifyAsync(target, cancellationToken);
        if (!foregroundResult.Success) return SessionStartResult.Failed(foregroundResult.Code);

        Guid sessionId = Guid.NewGuid();
        BrokerLaunchResult brokerResult = await broker.StartAndArmAsync(target, sessionId, cancellationToken);
        if (!brokerResult.Success)
            return SessionStartResult.Failed(brokerResult.Code);

        ForegroundResult postBrokerForeground =
            await foreground.ActivateAndVerifyAsync(target, cancellationToken);
        if (!postBrokerForeground.Success)
        {
            if (brokerResult.Connection is not null)
                await brokerResult.Connection.DisposeAsync();
            return SessionStartResult.Failed(postBrokerForeground.Code);
        }

        return new SessionStartResult(
            true,
            "SESSION_PREPARED",
            sessionId,
            target,
            brokerResult.Connection,
            facingResult.Direction,
            facingResult.Source);
    }
}
