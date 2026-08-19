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
        var random = new SequenceRandomSource(16, 27_438, 123, 47, 87, 101);
        StationaryAttackConfig config = StationaryAttackConfig.Default with { RestEnabled = false };
        var controller = CreateController(actions, publisher, scheduler, random, config);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

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
            new SequenceRandomSource(16, 20_001, 80, 30, 81, 80),
            StationaryAttackConfig.Default with { RestEnabled = false });

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 2, CancellationToken.None);

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
            new SequenceRandomSource(16, 27_438, 80, 30, 81, 80),
            StationaryAttackConfig.Default with { RestEnabled = false });

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal(27_438, actions.Leases[0]);
        Assert.Equal(80, actions.Leases[1]);
        Assert.Equal(81, actions.Leases[2]);
    }

    [Fact]
    public async Task Rechecks_safety_during_a_long_hold_and_stops_immediately()
    {
        var actions = new RecordingActionSink();
        var publisher = new RecordingPublisher();
        var scheduler = new AdvancingScheduler();
        var random = new SequenceRandomSource(16, 27_438);
        var controller = new StationarySessionController(
            actions,
            new RejectAfterFirstCheckGate(),
            scheduler,
            new FixedConfigProvider(StationaryAttackConfig.Default with { RestEnabled = false }),
            new WeightedAttackDurationSampler(random),
            new StationaryMovementPlanner(random),
            new AlwaysAttackTriggerStrategy(),
            random,
            publisher);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal(["Down:Attack", "Up:Attack", "ReleaseAll"], actions.Events);
        Assert.Equal(100, scheduler.NowMonoMs - 10_000);
        Assert.Equal("FOCUS_LOST", publisher.States[^1].EarlyReleaseReason);
    }

    [Fact]
    public async Task Reads_a_hot_update_only_at_the_next_complete_cycle_boundary()
    {
        var actions = new RecordingActionSink();
        StationaryAttackConfig first = StationaryAttackConfig.Default with
        {
            AttackBands = FixedBands(1_000),
            MoveHoldMinMs = 80,
            MoveHoldMaxMs = 80,
            MoveGapMinMs = 30,
            MoveGapMaxMs = 30,
            StabilizeMinMs = 80,
            StabilizeMaxMs = 80,
            RestEnabled = false
        };
        StationaryAttackConfig updated = first with
        {
            AttackBands = FixedBands(2_000),
            MoveHoldMinMs = 90,
            MoveHoldMaxMs = 90,
            MoveGapMinMs = 40,
            MoveGapMaxMs = 40,
            StabilizeMinMs = 90,
            StabilizeMaxMs = 90
        };
        var provider = new SequencedConfigProvider(first, updated);
        var random = new SequenceRandomSource(
            1, 1_000, 80, 30, 80, 80,
            1, 2_000, 90, 40, 90, 90);
        var controller = new StationarySessionController(
            actions,
            new AlwaysSafeGate(),
            new AdvancingScheduler(),
            provider,
            new WeightedAttackDurationSampler(random),
            new StationaryMovementPlanner(random),
            new AlwaysAttackTriggerStrategy(),
            random,
            new RecordingPublisher());

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 2, CancellationToken.None);

        Assert.Equal(2, provider.ReadCount);
        Assert.Equal([1_000, 80, 80, 2_000, 90, 90], actions.Leases);
    }

    [Fact]
    public async Task Publishes_a_stable_stop_code_when_hot_reload_exhausts_the_required_direction_budget()
    {
        StationaryAttackConfig first = FixedAttackConfig(250, 125, 80);
        StationaryAttackConfig reducedBudget = FixedAttackConfig(80, 80, 80);
        var publisher = new RecordingPublisher();
        var random = new SequenceRandomSource(
            1, 1_000, 125, 30, 80, 80,
            1, 1_000, 125, 30, 80, 80,
            1, 1_000);
        var controller = new StationarySessionController(
            new RecordingActionSink(),
            new AlwaysSafeGate(),
            new AdvancingScheduler(),
            new SequencedConfigProvider(first, first, reducedBudget),
            new WeightedAttackDurationSampler(random),
            new StationaryMovementPlanner(random),
            new AlwaysAttackTriggerStrategy(),
            random,
            publisher);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Left, cycleLimit: 3, CancellationToken.None);

        Assert.Equal("INITIAL_FACING_BUDGET_EXHAUSTED", publisher.States[^1].EarlyReleaseReason);
    }

    private static StationaryAttackConfig FixedAttackConfig(int maximumOffset, int firstHold, int secondHold) =>
        StationaryAttackConfig.Default with
        {
            AttackBands =
            [
                new AttackBand(1_000, 1_000, 25),
                new AttackBand(1_000, 1_000, 25),
                new AttackBand(1_000, 1_000, 25),
                new AttackBand(1_000, 1_000, 25)
            ],
            MaxLateralMoveMs = maximumOffset,
            MoveHoldMinMs = secondHold,
            MoveHoldMaxMs = firstHold,
            MoveGapMinMs = 30,
            MoveGapMaxMs = 30,
            StabilizeMinMs = 80,
            StabilizeMaxMs = 80,
            RestEnabled = false
        };

    private static AttackBand[] FixedBands(int durationMs) =>
    [
        new AttackBand(durationMs, durationMs, 25),
        new AttackBand(durationMs, durationMs, 25),
        new AttackBand(durationMs, durationMs, 25),
        new AttackBand(durationMs, durationMs, 25)
    ];

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

    private sealed class RejectAfterFirstCheckGate : IStationarySafetyGate
    {
        private int checks;
        public Task<SafetyCheckResult> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(++checks == 1
                ? SafetyCheckResult.Allowed()
                : SafetyCheckResult.Rejected("FOCUS_LOST"));
    }

    private sealed class SequencedConfigProvider(
        params StationaryAttackConfig[] configs) : IStationaryConfigProvider
    {
        public int ReadCount { get; private set; }

        public StationaryAttackConfig GetValidatedSnapshot()
        {
            ReadCount++;
            return configs[Math.Min(ReadCount - 1, configs.Length - 1)];
        }
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
