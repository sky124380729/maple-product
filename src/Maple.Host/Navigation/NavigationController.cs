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
        int preflightEmptyWaits = 0;
        bool preflightComplete = false;
        long? preflightStartedAt = null;
        int unresolvedPlatformFrames = 0;
        int connectorPulses = 0;
        int? lastKnownPlatform = null;
        long movementFeedbackNotBefore = long.MinValue;
        try
        {
            Publish(sessionId, NavigationPhase.Preflight, null, null, [], null, null);
            while (!cancellationToken.IsCancellationRequested)
            {
                string? safetyFault = safety.Evaluate();
                if (safetyFault is not null) return Stop(sessionId, safetyFault);
                NavigationObservation? observation = await observations.WaitForNewerAsync(sequence, cancellationToken);
                if (observation is null)
                {
                    if (!preflightComplete && ++preflightEmptyWaits < 5) continue;
                    return Stop(sessionId, "OBSERVATION_STALE");
                }
                preflightEmptyWaits = 0;
                NavigationLocalization localization = observation.Localization;
                if (localization.FrameSequence <= sequence) continue;
                sequence = localization.FrameSequence;
                if (!preflightComplete)
                {
                    preflightStartedAt ??= localization.CapturedAtMonoMs;
                    if (localization.CapturedAtMonoMs - preflightStartedAt.Value > 3_000)
                        return Stop(sessionId, "OBSERVATION_STALE", localization);
                }
                if (!observation.PackageHashValid) return Stop(sessionId, "MAP_PACKAGE_CHANGED");
                if (!localization.MapMatched)
                {
                    if (localization.FaultCode == "MAP_VALIDATION_PENDING")
                    {
                        Publish(sessionId,
                            preflightComplete ? NavigationPhase.VerifyingArrival : NavigationPhase.Preflight,
                            localization.PlatformId, null, [], null,
                            localization.FaultCode, localization);
                        continue;
                    }
                    return Stop(sessionId, localization.FaultCode ?? "MAP_MISMATCH", localization);
                }
                preflightComplete = true;
                if (previousAction is not null
                    && previousAction != NavigationInputAction.Attack
                    && localization.CapturedAtMonoMs < movementFeedbackNotBefore)
                {
                    Publish(sessionId, NavigationPhase.VerifyingArrival,
                        localization.PlatformId, null, [], null, null, localization);
                    continue;
                }
                if (localization.Self is null) return Stop(sessionId, "SELF_NOT_LOCALIZED", localization);
                int? horizontalRecoveryPlatform = localization.PlatformId is null
                    && previousAction is not NavigationInputAction.MoveUp and not NavigationInputAction.MoveDown
                        ? FindSameHeightRecoveryPlatform(localization.Self)
                        : null;
                int? verticalRecoveryPlatform = localization.PlatformId is null
                    && previousAction is not NavigationInputAction.MoveUp and not NavigationInputAction.MoveDown
                    && IsOnKnownLadder(localization.Self)
                        ? FindSafePlatformBelow(localization.Self)
                        : null;
                if (localization.PlatformId is null
                    && horizontalRecoveryPlatform is null
                    && verticalRecoveryPlatform is null
                    && previousAction is not NavigationInputAction.MoveUp and not NavigationInputAction.MoveDown)
                {
                    unresolvedPlatformFrames++;
                    if (unresolvedPlatformFrames < 3)
                    {
                        Publish(sessionId, NavigationPhase.VerifyingArrival, null, null, [], null,
                            "SELF_NOT_LOCALIZED", localization);
                        continue;
                    }
                    return Stop(sessionId, "SELF_NOT_LOCALIZED", localization);
                }
                if (localization.PlatformId is not null) unresolvedPlatformFrames = 0;

                if (previous is not null && previousAction is not null && previousAction != NavigationInputAction.Attack)
                {
                    double movement = Math.Sqrt(Math.Pow(localization.Self.X - previous.X, 2) + Math.Pow(localization.Self.Y - previous.Y, 2));
                    noProgress = movement < 0.75 ? noProgress + 1 : 0;
                    if (noProgress >= 3) return Stop(sessionId, "NAVIGATION_STUCK");
                }

                if (actionCount >= maxActions) return Stop(sessionId, "ACTION_LIMIT_REACHED");
                int? currentPlatform = localization.PlatformId;
                if (currentPlatform is int knownPlatform)
                {
                    if (lastKnownPlatform is int previousPlatform && previousPlatform != knownPlatform)
                        connectorPulses = 0;
                    lastKnownPlatform = knownPlatform;
                }
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
                    if (horizontalRecoveryPlatform is int sameHeightPlatform)
                    {
                        MapPlatform recoveryTarget = map.Platforms.Single(platform => platform.Id == sameHeightPlatform);
                        targetPlatform = sameHeightPlatform;
                        nextAction = localization.Self.X < recoveryTarget.XMin
                            ? NavigationInputAction.MoveRight
                            : NavigationInputAction.MoveLeft;
                        phase = NavigationPhase.Walking;
                    }
                    else if (verticalRecoveryPlatform is int safePlatform)
                    {
                        targetPlatform = safePlatform;
                        nextAction = NavigationInputAction.MoveDown;
                        phase = NavigationPhase.TraversingConnector;
                    }
                    else if (previousAction is not NavigationInputAction.MoveUp and not NavigationInputAction.MoveDown)
                        return Stop(sessionId, "SELF_NOT_LOCALIZED");
                    else
                    {
                        nextAction = previousAction.Value;
                        phase = NavigationPhase.TraversingConnector;
                    }
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

                if (nextAction is NavigationInputAction.MoveUp or NavigationInputAction.MoveDown)
                {
                    connectorPulses++;
                    if (connectorPulses > 20) return Stop(sessionId, "CONNECTOR_TIMEOUT", localization);
                }
                else connectorPulses = 0;

                Publish(sessionId, phase, currentPlatform, targetPlatform, routeIds, nextAction, null, localization);
                previous = localization.Self;
                previousAction = nextAction;
                actionCount++;
                InputActionResult input = await PulseAsync(nextAction, cancellationToken);
                if (!input.Success) return Stop(sessionId, input.Code);
                movementFeedbackNotBefore = nextAction == NavigationInputAction.Attack
                    ? long.MinValue
                    : localization.CapturedAtMonoMs + DurationFor(nextAction) + 150;
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
        int duration = DurationFor(action);
        InputActionResult down = await actions.KeyDownAsync(action, duration + 100, token);
        if (!down.Success) return down;
        try { await delay.DelayAsync(duration, token); }
        finally { await actions.KeyUpAsync(action, CancellationToken.None); }
        return down;
    }

    private static int DurationFor(NavigationInputAction action) => action switch
    {
        NavigationInputAction.Attack => 250,
        NavigationInputAction.MoveUp or NavigationInputAction.MoveDown => 100,
        _ => 80
    };

    private int? FindSafePlatformBelow(MapPoint self)
    {
        MapPlatform[] below = map.Platforms.Where(platform =>
            platform.Y > self.Y
            && platform.Y - self.Y <= 40).ToArray();
        MapPlatform[] covered = below.Where(platform => HorizontalDistance(self.X, platform) == 0).ToArray();
        MapPlatform[] candidates = covered.Length > 0
            ? covered
            : below.Where(platform => HorizontalDistance(self.X, platform) <= 12).ToArray();
        if (candidates.Length == 0) return null;
        double nearestY = candidates.Min(platform => platform.Y);
        MapPlatform[] nearest = candidates.Where(platform =>
            Math.Abs(platform.Y - nearestY) <= 0.5).ToArray();
        double nearestHorizontal = nearest.Min(platform => HorizontalDistance(self.X, platform));
        MapPlatform[] safest = nearest.Where(platform =>
            Math.Abs(HorizontalDistance(self.X, platform) - nearestHorizontal) <= 0.5).ToArray();
        return safest.Length == 1 ? safest[0].Id : null;
    }

    private int? FindSameHeightRecoveryPlatform(MapPoint self)
    {
        MapPlatform[] candidates = map.Platforms.Where(platform =>
        {
            double horizontalDistance = HorizontalDistance(self.X, platform);
            return Math.Abs(platform.Y - self.Y) <= 5
                && horizontalDistance > 0
                && horizontalDistance <= 12;
        }).ToArray();
        if (candidates.Length == 0) return null;
        double nearestDistance = candidates.Min(platform => HorizontalDistance(self.X, platform));
        MapPlatform[] nearest = candidates.Where(platform =>
            Math.Abs(HorizontalDistance(self.X, platform) - nearestDistance) <= 0.5).ToArray();
        return nearest.Length == 1 ? nearest[0].Id : null;
    }

    private bool IsOnKnownLadder(MapPoint self) => map.Ladders.Any(ladder =>
        Math.Abs(self.X - ladder.X) <= 3
        && self.Y >= ladder.YMin - 3
        && self.Y <= ladder.YMax + 3);

    private static double HorizontalDistance(double x, MapPlatform platform) =>
        x < platform.XMin ? platform.XMin - x : x > platform.XMax ? x - platform.XMax : 0;

    private NavigationStop Stop(string sessionId, string code, NavigationLocalization? localization = null)
    {
        Publish(sessionId, NavigationPhase.Stopped, localization?.PlatformId, null, [], null, code, localization);
        return new NavigationStop(code);
    }

    private void Publish(
        string sessionId, NavigationPhase phase, int? current, int? target,
        ImmutableArray<int> route, NavigationInputAction? action, string? fault,
        NavigationLocalization? localization = null) =>
        publisher.Publish(new NavigationState(
            sessionId, phase, current, target, route, action, fault,
            localization?.MatchConfidence,
            localization?.Self));
}
