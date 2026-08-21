using System.Collections.Immutable;
using Maple.Host.Stationary;

namespace Maple.Host.Navigation;

public sealed class NavigationController(
    MapPackageSnapshot map,
    NavigationGraph graph,
    INavigationObservationSource observations,
    INavigationActionSink actions,
    INavigationDelay delay,
    INavigationSafetyGate safety,
    INavigationStatePublisher publisher,
    int maxActions = int.MaxValue)
{
    public async Task<NavigationStop> RunAsync(string sessionId, CancellationToken cancellationToken)
    {
        PatrolTargetSelector patrol = new(graph);
        long sequence = -1;
        MapPoint? previous = null;
        NavigationInputAction? previousAction = null;
        int noProgress = 0;
        int actionCount = 0;
        try
        {
            Publish(sessionId, NavigationPhase.Preflight, null, null, [], null, null);
            while (!cancellationToken.IsCancellationRequested)
            {
                string? safetyFault = safety.Evaluate();
                if (safetyFault is not null) return Stop(sessionId, safetyFault);
                NavigationObservation? observation = await observations.WaitForNewerAsync(sequence, cancellationToken);
                if (observation is null) return Stop(sessionId, "OBSERVATION_STALE");
                NavigationLocalization localization = observation.Localization;
                if (localization.FrameSequence <= sequence) continue;
                sequence = localization.FrameSequence;
                if (!observation.PackageHashValid) return Stop(sessionId, "MAP_PACKAGE_CHANGED");
                if (!localization.MapMatched) return Stop(sessionId, localization.FaultCode ?? "MAP_MISMATCH");
                if (localization.Self is null) return Stop(sessionId, "SELF_NOT_LOCALIZED");
                if (localization.PlatformId is null && previousAction is not NavigationInputAction.MoveUp and not NavigationInputAction.MoveDown)
                    return Stop(sessionId, "SELF_NOT_LOCALIZED");

                if (previous is not null && previousAction is not null)
                {
                    double movement = Math.Sqrt(Math.Pow(localization.Self.X - previous.X, 2) + Math.Pow(localization.Self.Y - previous.Y, 2));
                    noProgress = movement < 0.75 ? noProgress + 1 : 0;
                    if (noProgress >= 3) return Stop(sessionId, "NAVIGATION_STUCK");
                }

                if (actionCount >= maxActions) return Stop(sessionId, "ACTION_LIMIT_REACHED");
                int? currentPlatform = localization.PlatformId;
                AuthorizedMonster? monster = currentPlatform is null ? null : observation.Monsters
                    .Where(item => item.PlatformId == currentPlatform)
                    .OrderBy(item => item.DistanceToSelf).FirstOrDefault();
                NavigationInputAction nextAction;
                NavigationPhase phase;
                int? targetPlatform = null;
                ImmutableArray<int> routeIds = [];

                if (monster is not null)
                {
                    targetPlatform = currentPlatform;
                    if (monster.DistanceToSelf <= map.Thresholds.AttackRangePixels)
                    {
                        nextAction = NavigationInputAction.Attack;
                        phase = NavigationPhase.Combat;
                    }
                    else if (observation.SelfScreenX is double screenX)
                    {
                        nextAction = monster.Bounds.X + monster.Bounds.Width / 2 < screenX
                            ? NavigationInputAction.MoveLeft
                            : NavigationInputAction.MoveRight;
                        phase = NavigationPhase.Walking;
                    }
                    else return Stop(sessionId, "SELF_NOT_LOCALIZED");
                }
                else if (currentPlatform is null)
                {
                    if (previousAction is not NavigationInputAction.MoveUp and not NavigationInputAction.MoveDown)
                        return Stop(sessionId, "SELF_NOT_LOCALIZED");
                    nextAction = previousAction.Value;
                    phase = NavigationPhase.TraversingConnector;
                }
                else
                {
                    patrol.ConfirmArrival(currentPlatform.Value, localization.CapturedAtMonoMs);
                    targetPlatform = patrol.Select(currentPlatform.Value, localization.Self.X);
                    NavigationRoute route = graph.FindRoute(currentPlatform.Value, targetPlatform.Value, localization.Self.X);
                    if (!route.Success || route.Edges.IsEmpty) return Stop(sessionId, "MAP_GRAPH_UNSUPPORTED");
                    routeIds = route.PlatformIds;
                    NavigationEdge edge = route.Edges[0];
                    double delta = edge.ApproachX - localization.Self.X;
                    if (Math.Abs(delta) > 3)
                    {
                        nextAction = delta < 0 ? NavigationInputAction.MoveLeft : NavigationInputAction.MoveRight;
                        phase = NavigationPhase.AligningConnector;
                    }
                    else
                    {
                        nextAction = edge.Direction == NavigationVerticalDirection.Up
                            ? NavigationInputAction.MoveUp
                            : NavigationInputAction.MoveDown;
                        phase = NavigationPhase.TraversingConnector;
                    }
                }

                Publish(sessionId, phase, currentPlatform, targetPlatform, routeIds, nextAction, null);
                previous = localization.Self;
                previousAction = nextAction;
                actionCount++;
                InputActionResult input = await PulseAsync(nextAction, cancellationToken);
                if (!input.Success) return Stop(sessionId, input.Code);
            }
            return Stop(sessionId, "CANCELLED");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Stop(sessionId, "CANCELLED");
        }
        finally
        {
            await actions.ReleaseAllAsync(CancellationToken.None);
        }
    }

    private async Task<InputActionResult> PulseAsync(NavigationInputAction action, CancellationToken token)
    {
        int duration = action switch
        {
            NavigationInputAction.Attack => 250,
            NavigationInputAction.MoveUp or NavigationInputAction.MoveDown => 100,
            _ => 80
        };
        InputActionResult down = await actions.KeyDownAsync(action, duration + 100, token);
        if (!down.Success) return down;
        try { await delay.DelayAsync(duration, token); }
        finally { await actions.KeyUpAsync(action, CancellationToken.None); }
        return down;
    }

    private NavigationStop Stop(string sessionId, string code)
    {
        Publish(sessionId, NavigationPhase.Stopped, null, null, [], null, code);
        return new NavigationStop(code);
    }

    private void Publish(
        string sessionId, NavigationPhase phase, int? current, int? target,
        ImmutableArray<int> route, NavigationInputAction? action, string? fault) =>
        publisher.Publish(new NavigationState(sessionId, phase, current, target, route, action, fault));
}
