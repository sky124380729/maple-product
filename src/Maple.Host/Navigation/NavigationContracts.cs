using System.Collections.Immutable;

namespace Maple.Host.Navigation;

public sealed record AuthorizedMonster(MonsterCandidate Bounds, int PlatformId, double DistanceToSelf);

public sealed record NavigationObservation(
    NavigationLocalization Localization,
    IReadOnlyList<AuthorizedMonster> Monsters,
    double? SelfScreenX,
    bool PackageHashValid);

public enum NavigationPhase
{
    Preflight, Patrolling, Planning, Walking, AligningConnector,
    TraversingConnector, VerifyingArrival, Combat, Stopped
}

public sealed record NavigationState(
    string SessionId,
    NavigationPhase Phase,
    int? CurrentPlatformId,
    int? TargetPlatformId,
    ImmutableArray<int> Route,
    NavigationInputAction? Action,
    string? FaultCode);

public sealed record NavigationStop(string Code);

public interface INavigationObservationSource
{
    Task<NavigationObservation?> WaitForNewerAsync(long afterSequence, CancellationToken token);
}

public interface INavigationSafetyGate { string? Evaluate(); }
public interface INavigationStatePublisher { void Publish(NavigationState state); }
public interface INavigationDelay { Task DelayAsync(int milliseconds, CancellationToken token); }

public sealed class SystemNavigationDelay : INavigationDelay
{
    public Task DelayAsync(int milliseconds, CancellationToken token) => Task.Delay(milliseconds, token);
}
