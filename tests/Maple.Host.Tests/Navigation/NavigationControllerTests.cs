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

    [Fact]
    public async Task Repeated_attacks_are_not_treated_as_navigation_stuck()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        AuthorizedMonster monster = new(new MonsterCandidate(100, 300, 30, 30, 0.9), 0, 40);
        ScriptedSource source = new(
            Observation(1, 0, 50, 70, [monster]), Observation(2, 0, 50, 70, [monster]),
            Observation(3, 0, 50, 70, [monster]), Observation(4, 0, 50, 70, [monster]));
        RecordingSink sink = new();

        NavigationStop result = await Controller(map, source, sink, maxActions: 3)
            .RunAsync("session", CancellationToken.None);

        Assert.Equal("ACTION_LIMIT_REACHED", result.Code);
        Assert.Equal(3, sink.Down.Count(action => action == NavigationInputAction.Attack));
    }

    [Fact]
    public async Task Preflight_waits_for_validation_without_sending_input()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        ScriptedSource source = new(
            PendingObservation(1),
            PendingObservation(2),
            Observation(3, 0, 50, 70));
        RecordingSink sink = new();

        NavigationStop result = await Controller(map, source, sink, maxActions: 0)
            .RunAsync("session", CancellationToken.None);

        Assert.Equal("ACTION_LIMIT_REACHED", result.Code);
        Assert.Empty(sink.Down);
    }

    [Fact]
    public async Task Preflight_publishes_validation_progress_with_logical_position()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        ScriptedSource source = new(PendingObservation(1), Observation(2, 0, 50, 70));
        RecordingSink sink = new();
        RecordingPublisher publisher = new();
        NavigationController controller = new(
            map, new NavigationGraph(map), source, sink, new ImmediateDelay(), new AlwaysSafe(), publisher, maxActions: 0);

        await controller.RunAsync("session", CancellationToken.None);

        NavigationState pending = Assert.Single(publisher.States, state => state.FaultCode == "MAP_VALIDATION_PENDING");
        Assert.Equal(0.8, pending.LocalizationConfidence);
        Assert.Equal(new MapPoint(50, 70), pending.Self);
    }

    [Fact]
    public async Task Runtime_validation_pending_frame_does_not_send_or_stop_input_loop()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        ScriptedSource source = new(
            Observation(1, 0, 50, 70),
            new NavigationObservation(
                new NavigationLocalization(2, 20, false, 0.4, null, null, "MAP_VALIDATION_PENDING"),
                [], null, true),
            Observation(3, 0, 55, 70),
            Observation(4, 0, 50, 70));
        RecordingSink sink = new();

        NavigationStop result = await Controller(map, source, sink, maxActions: 2)
            .RunAsync("session", CancellationToken.None);

        Assert.Equal("ACTION_LIMIT_REACHED", result.Code);
        Assert.Equal(2, sink.Down.Count);
    }

    [Fact]
    public async Task Preflight_allows_transient_empty_waits_before_first_frame()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        ScriptedSource source = new(
            null,
            null,
            Observation(1, 0, 50, 70));
        RecordingSink sink = new();

        NavigationStop result = await Controller(map, source, sink, maxActions: 0)
            .RunAsync("session", CancellationToken.None);

        Assert.Equal("ACTION_LIMIT_REACHED", result.Code);
        Assert.Empty(sink.Down);
    }

    [Fact]
    public async Task Preflight_stops_after_three_seconds_of_validation_pending()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        ScriptedSource source = new(
            PendingObservation(1, capturedAtMonoMs: 100),
            PendingObservation(2, capturedAtMonoMs: 3_101));
        RecordingSink sink = new();

        NavigationStop result = await Controller(map, source, sink, maxActions: 1)
            .RunAsync("session", CancellationToken.None);

        Assert.Equal("OBSERVATION_STALE", result.Code);
        Assert.Equal(2, source.CallCount);
        Assert.Empty(sink.Down);
    }

    [Fact]
    public async Task Waits_for_three_ambiguous_platform_frames_before_stopping()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        ScriptedSource source = new(
            Observation(1, null, 50, 70),
            Observation(2, null, 50, 70),
            Observation(3, 0, 50, 70));
        RecordingSink sink = new();

        NavigationStop result = await Controller(map, source, sink, maxActions: 0)
            .RunAsync("session", CancellationToken.None);

        Assert.Equal("ACTION_LIMIT_REACHED", result.Code);
        Assert.Empty(sink.Down);
    }

    [Fact]
    public async Task Does_not_descend_from_unmapped_perch_without_confirmed_ladder()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        ScriptedSource source = new(
            Observation(1, null, 100, 30),
            Observation(2, null, 100, 30),
            Observation(3, null, 100, 30));
        RecordingSink sink = new();

        NavigationStop result = await Controller(map, source, sink, maxActions: 2)
            .RunAsync("session", CancellationToken.None);

        Assert.Equal("SELF_NOT_LOCALIZED", result.Code);
        Assert.DoesNotContain(NavigationInputAction.MoveDown, sink.Down);
        Assert.Equal(1, sink.ReleaseAllCount);
    }

    [Fact]
    public async Task Descends_only_when_unmapped_position_is_on_known_ladder()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        ScriptedSource source = new(
            Observation(1, null, 50, 55),
            Observation(2, null, 50, 57),
            Observation(3, 1, 50, 60));
        RecordingSink sink = new();

        NavigationStop result = await Controller(map, source, sink, maxActions: 2)
            .RunAsync("session", CancellationToken.None);

        Assert.Equal("ACTION_LIMIT_REACHED", result.Code);
        Assert.Equal(
            [NavigationInputAction.MoveDown, NavigationInputAction.MoveDown],
            sink.Down);
    }

    [Fact]
    public async Task Moves_horizontally_to_unique_same_height_platform_from_unmapped_gap()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        ScriptedSource source = new(
            Observation(1, null, 96, 50),
            Observation(2, null, 93, 50),
            Observation(3, 2, 90, 50));
        RecordingSink sink = new();

        NavigationStop result = await Controller(map, source, sink, maxActions: 2)
            .RunAsync("session", CancellationToken.None);

        Assert.Equal("ACTION_LIMIT_REACHED", result.Code);
        Assert.Equal(
            [NavigationInputAction.MoveLeft, NavigationInputAction.MoveLeft],
            sink.Down);
    }

    [Fact]
    public async Task Waits_for_post_action_fresh_frame_before_sending_next_input()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        ScriptedSource source = new(
            Observation(1, 0, 10, 70, capturedAtMonoMs: 100),
            Observation(2, 0, 10, 70, capturedAtMonoMs: 150),
            Observation(3, 0, 50, 70, capturedAtMonoMs: 350),
            Observation(4, null, 50, 65, capturedAtMonoMs: 650));
        RecordingSink sink = new();

        NavigationStop result = await Controller(map, source, sink, maxActions: 2)
            .RunAsync("session", CancellationToken.None);

        Assert.Equal("ACTION_LIMIT_REACHED", result.Code);
        Assert.Equal(
            [NavigationInputAction.MoveRight, NavigationInputAction.MoveUp],
            sink.Down);
    }

    [Fact]
    public async Task Stops_connector_after_bounded_vertical_pulses()
    {
        MapPackageSnapshot map = NavigationGraphTests.SwampShape();
        var values = new List<NavigationObservation> { Observation(1, 0, 50, 70) };
        for (int sequence = 2; sequence <= 23; sequence++)
            values.Add(Observation(sequence, null, 50, 70 - sequence));
        ScriptedSource source = new([.. values]);
        RecordingSink sink = new();

        NavigationStop result = await Controller(map, source, sink, maxActions: 50)
            .RunAsync("session", CancellationToken.None);

        Assert.Equal("CONNECTOR_TIMEOUT", result.Code);
        Assert.Equal(20, sink.Down.Count(action => action == NavigationInputAction.MoveUp));
        Assert.Equal(1, sink.ReleaseAllCount);
    }

    private static NavigationController Controller(
        MapPackageSnapshot map, ScriptedSource source, RecordingSink sink, int maxActions) =>
        new(map, new NavigationGraph(map), source, sink, new ImmediateDelay(), new AlwaysSafe(), new NullPublisher(), maxActions);

    private static NavigationObservation Observation(
        long sequence,
        int? platform,
        double x,
        double y,
        IReadOnlyList<AuthorizedMonster>? monsters = null,
        long? capturedAtMonoMs = null) =>
        new(new NavigationLocalization(sequence, capturedAtMonoMs ?? sequence * 250, true, 1, new MapPoint(x, y), platform, null),
            monsters ?? [], SelfScreenX: 100, PackageHashValid: true);

    private static NavigationObservation PendingObservation(long sequence, long? capturedAtMonoMs = null) =>
        new(new NavigationLocalization(sequence, capturedAtMonoMs ?? sequence * 10, false, 0.8, new MapPoint(50, 70), 0, "MAP_VALIDATION_PENDING"),
            [], SelfScreenX: 100, PackageHashValid: true);

    private sealed class ScriptedSource(params NavigationObservation?[] values) : INavigationObservationSource
    {
        private readonly Queue<NavigationObservation?> queue = new(values);
        public int CallCount { get; private set; }
        public Task<NavigationObservation?> WaitForNewerAsync(long afterSequence, CancellationToken token)
        {
            CallCount++;
            return Task.FromResult(queue.TryDequeue(out NavigationObservation? value) ? value : null);
        }
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

    private sealed class RecordingPublisher : INavigationStatePublisher
    {
        public List<NavigationState> States { get; } = [];
        public void Publish(NavigationState state) => States.Add(state);
    }
}
