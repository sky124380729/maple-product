using Maple.Host.Navigation;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Navigation;

public sealed class SwampNavigationIntegrationTests
{
    [Fact]
    public async Task Traverses_all_platforms_attacks_authorized_monster_and_resumes_patrol()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        SimulatedEnvironment environment = new(map);
        SimulatedSource source = new(environment);
        SimulatedSink sink = new(environment);
        NavigationController controller = new(
            map,
            new NavigationGraph(map),
            source,
            sink,
            new ImmediateDelay(),
            new AlwaysSafe(),
            new NullPublisher(),
            maxActions: 20);

        NavigationStop result = await controller.RunAsync("swamp", CancellationToken.None);

        Assert.Equal("ACTION_LIMIT_REACHED", result.Code);
        Assert.Equal(Enumerable.Range(0, 7), environment.Visited.OrderBy(id => id));
        Assert.Contains(NavigationInputAction.MoveUp, sink.Down);
        Assert.Contains(NavigationInputAction.MoveDown, sink.Down);
        Assert.Single(sink.Down.Where(action => action == NavigationInputAction.Attack));
        Assert.True(environment.PatrolResumedAfterAttack);
        Assert.Equal(1, sink.ReleaseAllCount);
    }

    private sealed class SimulatedEnvironment(MapPackageSnapshot map)
    {
        private readonly IReadOnlyDictionary<int, MapPlatform> platforms = map.Platforms.ToDictionary(item => item.Id);
        public int PlatformId { get; private set; } = 3;
        public double X { get; private set; } = 50;
        public bool MonsterAvailable { get; private set; } = true;
        public bool Attacked { get; private set; }
        public bool PatrolResumedAfterAttack { get; private set; }
        public HashSet<int> Visited { get; } = [];

        public NavigationObservation Observe(long sequence)
        {
            Visited.Add(PlatformId);
            if (Attacked && PlatformId != 6) PatrolResumedAfterAttack = true;
            IReadOnlyList<AuthorizedMonster> monsters = PlatformId == 6 && MonsterAvailable
                ? [new AuthorizedMonster(new MonsterCandidate(110, 200, 24, 20, 0.9), 6, 40)]
                : [];
            return new NavigationObservation(
                new NavigationLocalization(
                    sequence,
                    sequence * 100,
                    true,
                    1,
                    new MapPoint(X, platforms[PlatformId].Y),
                    PlatformId,
                    null),
                monsters,
                SelfScreenX: 100,
                PackageHashValid: true);
        }

        public void Apply(NavigationInputAction action)
        {
            switch (action)
            {
                case NavigationInputAction.MoveLeft:
                    X -= 25;
                    break;
                case NavigationInputAction.MoveRight:
                    X += 25;
                    break;
                case NavigationInputAction.MoveUp:
                    Traverse(up: true);
                    break;
                case NavigationInputAction.MoveDown:
                    Traverse(up: false);
                    break;
                case NavigationInputAction.Attack:
                    Assert.Equal(6, PlatformId);
                    Assert.True(MonsterAvailable);
                    MonsterAvailable = false;
                    Attacked = true;
                    break;
            }
        }

        private void Traverse(bool up)
        {
            MapLadder ladder = Assert.Single(map.Ladders.Where(item =>
                item.PlatformIds.Contains(PlatformId)
                && Math.Abs(item.X - X) <= 3
                && item.PlatformIds.Any(other => other != PlatformId
                    && (up ? platforms[other].Y < platforms[PlatformId].Y : platforms[other].Y > platforms[PlatformId].Y))));
            PlatformId = ladder.PlatformIds.Single(id => id != PlatformId);
            X = ladder.X;
        }
    }

    private sealed class SimulatedSource(SimulatedEnvironment environment) : INavigationObservationSource
    {
        private long sequence;
        public Task<NavigationObservation?> WaitForNewerAsync(long afterSequence, CancellationToken token) =>
            Task.FromResult<NavigationObservation?>(environment.Observe(++sequence));
    }

    private sealed class SimulatedSink(SimulatedEnvironment environment) : INavigationActionSink
    {
        public List<NavigationInputAction> Down { get; } = [];
        public int ReleaseAllCount { get; private set; }

        public Task<InputActionResult> KeyDownAsync(NavigationInputAction action, int leaseMs, CancellationToken token)
        {
            Down.Add(action);
            environment.Apply(action);
            return Task.FromResult(InputActionResult.Ok("OK"));
        }

        public Task<InputActionResult> KeyUpAsync(NavigationInputAction action, CancellationToken token) =>
            Task.FromResult(InputActionResult.Ok("OK"));

        public Task<InputActionResult> ReleaseAllAsync(CancellationToken token)
        {
            ReleaseAllCount++;
            return Task.FromResult(InputActionResult.Ok("OK"));
        }
    }

    private sealed class ImmediateDelay : INavigationDelay
    {
        public Task DelayAsync(int milliseconds, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class AlwaysSafe : INavigationSafetyGate
    {
        public string? Evaluate() => null;
    }

    private sealed class NullPublisher : INavigationStatePublisher
    {
        public void Publish(NavigationState state) { }
    }
}
