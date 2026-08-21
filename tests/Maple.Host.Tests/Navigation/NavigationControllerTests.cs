using Maple.Host.Navigation;
using Maple.Host.Recognition;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Navigation;

public sealed class NavigationControllerTests
{
    [Fact]
    public async Task Walks_to_ladder_climbs_and_confirms_arrival()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        ScriptedSource source = new(
            Observation(1, 0, 40, 70),
            Observation(2, 0, 48, 70),
            Observation(3, 0, 50, 70),
            Observation(4, null, 50, 65),
            Observation(5, 1, 50, 60));
        RecordingSink sink = new();
        NavigationController controller = Controller(map, source, sink, maxActions: 4);

        NavigationStop result = await controller.RunAsync("session", CancellationToken.None);

        Assert.Contains(NavigationInputAction.MoveRight, sink.Down);
        Assert.Contains(NavigationInputAction.MoveUp, sink.Down);
        Assert.Equal("ACTION_LIMIT_REACHED", result.Code);
        Assert.Equal(1, sink.ReleaseAllCount);
    }

    [Fact]
    public async Task Attacks_only_authorized_same_platform_monster()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        AuthorizedMonster monster = new(new MonsterCandidate(100, 300, 30, 30, 0.9), 0, 40);
        ScriptedSource source = new(
            Observation(1, 0, 50, 70, [monster]),
            Observation(2, 0, 50, 70));
        RecordingSink sink = new();

        await Controller(map, source, sink, maxActions: 1).RunAsync("session", CancellationToken.None);

        Assert.Equal([NavigationInputAction.Attack], sink.Down);
    }

    [Fact]
    public async Task Stops_after_three_no_progress_pulses_and_releases_all()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        ScriptedSource source = new(
            Observation(1, 0, 10, 70), Observation(2, 0, 10, 70),
            Observation(3, 0, 10, 70), Observation(4, 0, 10, 70));
        RecordingSink sink = new();

        NavigationStop result = await Controller(map, source, sink, maxActions: 20)
            .RunAsync("session", CancellationToken.None);

        Assert.Equal("NAVIGATION_STUCK", result.Code);
        Assert.Equal(1, sink.ReleaseAllCount);
    }

    private static NavigationController Controller(
        MapPackageSnapshot map, ScriptedSource source, RecordingSink sink, int maxActions) =>
        new(map, new NavigationGraph(map), source, sink, new ImmediateDelay(), new AlwaysSafe(), new NullPublisher(), maxActions);

    private static NavigationObservation Observation(
        long sequence, int? platform, double x, double y, IReadOnlyList<AuthorizedMonster>? monsters = null) =>
        new(new NavigationLocalization(sequence, sequence * 10, true, 1, new MapPoint(x, y), platform, null),
            monsters ?? [], SelfScreenX: 100, PackageHashValid: true);

    private sealed class ScriptedSource(params NavigationObservation[] values) : INavigationObservationSource
    {
        private readonly Queue<NavigationObservation> queue = new(values);
        public Task<NavigationObservation?> WaitForNewerAsync(long afterSequence, CancellationToken token) =>
            Task.FromResult(queue.TryDequeue(out NavigationObservation? value) ? value : null);
    }

    private sealed class RecordingSink : INavigationActionSink
    {
        public List<NavigationInputAction> Down { get; } = [];
        public int ReleaseAllCount { get; private set; }
        public Task<InputActionResult> KeyDownAsync(NavigationInputAction action, int leaseMs, CancellationToken token)
        { Down.Add(action); return Task.FromResult(InputActionResult.Ok("OK")); }
        public Task<InputActionResult> KeyUpAsync(NavigationInputAction action, CancellationToken token) =>
            Task.FromResult(InputActionResult.Ok("OK"));
        public Task<InputActionResult> ReleaseAllAsync(CancellationToken token)
        { ReleaseAllCount++; return Task.FromResult(InputActionResult.Ok("OK")); }
    }

    private sealed class ImmediateDelay : INavigationDelay
    { public Task DelayAsync(int milliseconds, CancellationToken token) => Task.CompletedTask; }
    private sealed class AlwaysSafe : INavigationSafetyGate
    { public string? Evaluate() => null; }
    private sealed class NullPublisher : INavigationStatePublisher
    { public void Publish(NavigationState state) { } }
}
