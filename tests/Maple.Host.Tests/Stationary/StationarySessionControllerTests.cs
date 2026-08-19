using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;
using Maple.Core.Session;
using Maple.Core.Triggers;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class StationarySessionControllerTests
{
    [Fact]
    public async Task Runs_one_complete_cycle_in_strict_key_order()
    {
        var actions = new RecordingActionSink();
        var publisher = new RecordingPublisher();
        var scheduler = new AdvancingScheduler();
        var random = new SequenceRandomSource(16, 27_438, 0, 123, 47, 87, 101);
        StationaryAttackConfig config = StationaryAttackConfig.Default with { RestEnabled = false };
        var controller = CreateController(actions, publisher, scheduler, random, config);

        await controller.RunAsync(Guid.NewGuid(), cycleLimit: 1, CancellationToken.None);

        Assert.Equal(
            [
                "Down:Attack", "Up:Attack",
                "Down:MoveLeft", "Up:MoveLeft",
                "Down:MoveRight", "Up:MoveRight",
                "ReleaseAll"
            ],
            actions.Events);
        Assert.Equal(
            [
                StationaryPhase.AttackHolding,
                StationaryPhase.MoveFirst,
                StationaryPhase.MoveGap,
                StationaryPhase.MoveSecond,
                StationaryPhase.Stabilizing,
                StationaryPhase.Stopped
            ],
            publisher.States.Select(state => state.Phase));
        StationaryRhythmState attack = publisher.States[0];
        Assert.Equal(27_438, attack.SampledDurationMs);
        Assert.Equal(attack.PhaseStartedMonoMs + 27_438, attack.PhaseDeadlineMonoMs);
    }

    [Fact]
    public async Task Stops_and_releases_all_when_second_direction_key_up_fails()
    {
        var actions = new RecordingActionSink(failEvent: "Up:MoveRight");
        var publisher = new RecordingPublisher();
        var controller = CreateController(
            actions,
            publisher,
            new AdvancingScheduler(),
            new SequenceRandomSource(16, 20_001, 0, 80, 30, 81, 80),
            StationaryAttackConfig.Default with { RestEnabled = false });

        await controller.RunAsync(Guid.NewGuid(), cycleLimit: 2, CancellationToken.None);

        Assert.Equal("ReleaseAll", actions.Events[^1]);
        Assert.Equal(1, actions.Events.Count(item => item == "Down:Attack"));
        Assert.Equal("KEY_UP_FAILED", publisher.States[^1].EarlyReleaseReason);
    }

    [Fact]
    public async Task Sends_the_sampled_hold_duration_as_the_broker_lease()
    {
        var actions = new RecordingActionSink();
        var controller = CreateController(
            actions,
            new RecordingPublisher(),
            new AdvancingScheduler(),
            new SequenceRandomSource(16, 27_438, 0, 80, 30, 81, 80),
            StationaryAttackConfig.Default with { RestEnabled = false });

        await controller.RunAsync(Guid.NewGuid(), cycleLimit: 1, CancellationToken.None);

        Assert.Equal(27_438, actions.Leases[0]);
        Assert.Equal(80, actions.Leases[1]);
        Assert.Equal(81, actions.Leases[2]);
    }

    private static StationarySessionController CreateController(
        RecordingActionSink actions,
        RecordingPublisher publisher,
        AdvancingScheduler scheduler,
        IRandomSource random,
        StationaryAttackConfig config) =>
        new(
            actions,
            new AlwaysSafeGate(),
            scheduler,
            new FixedConfigProvider(config),
            new WeightedAttackDurationSampler(random),
            new StationaryMovementPlanner(random),
            new AlwaysAttackTriggerStrategy(),
            random,
            publisher);

    private sealed class RecordingActionSink(string? failEvent = null) : IStationaryActionSink
    {
        public List<string> Events { get; } = [];
        public List<int> Leases { get; } = [];

        public Task<InputActionResult> KeyDownAsync(StationaryInputAction action, int leaseMs, CancellationToken cancellationToken)
        {
            Leases.Add(leaseMs);
            return Record($"Down:{action}");
        }

        public Task<InputActionResult> KeyUpAsync(StationaryInputAction action, CancellationToken cancellationToken) =>
            Record($"Up:{action}");

        public Task<InputActionResult> ReleaseAllAsync(CancellationToken cancellationToken)
        {
            Events.Add("ReleaseAll");
            return Task.FromResult(InputActionResult.Ok("ALL_KEYS_RELEASED"));
        }

        private Task<InputActionResult> Record(string value)
        {
            Events.Add(value);
            return Task.FromResult(value == failEvent
                ? InputActionResult.Fail("KEY_UP_FAILED")
                : InputActionResult.Ok("OK"));
        }
    }

    private sealed class AlwaysSafeGate : IStationarySafetyGate
    {
        public Task<SafetyCheckResult> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(SafetyCheckResult.Allowed());
    }

    private sealed class AdvancingScheduler : IMonotonicScheduler
    {
        public long NowMonoMs { get; private set; } = 10_000;

        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            NowMonoMs += milliseconds;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedConfigProvider(StationaryAttackConfig config) : IStationaryConfigProvider
    {
        public StationaryAttackConfig GetValidatedSnapshot() => config;
    }

    private sealed class RecordingPublisher : IStationaryStatePublisher
    {
        public List<StationaryRhythmState> States { get; } = [];
        public void Publish(StationaryRhythmState state) => States.Add(state);
    }

    private sealed class SequenceRandomSource(params int[] values) : IRandomSource
    {
        private readonly Queue<int> values = new(values);
        public int NextInclusive(int minimum, int maximum)
        {
            int value = values.Dequeue();
            Assert.InRange(value, minimum, maximum);
            return value;
        }
    }
}
