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
    public void Input_action_result_carries_broker_movement_timing()
    {
        InputActionResult result = InputActionResult.Ok(
            "KEY_ALREADY_UP",
            actualHoldMs: 46,
            releaseLatenessMs: 6);

        Assert.Equal(46, result.ActualHoldMs);
        Assert.Equal(6, result.ReleaseLatenessMs);
    }

    [Fact]
    public async Task Publishes_actual_offset_after_each_segment_and_plans_second_from_actual_first()
    {
        var actions = new RecordingActionSink();
        actions.EnqueueMovementUp(InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 46, releaseLatenessMs: 6));
        actions.EnqueueMovementUp(InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 30, releaseLatenessMs: 0));
        var publisher = new RecordingPublisher();
        var controller = CreateController(
            actions,
            publisher,
            new AdvancingScheduler(),
            new SequenceRandomSource(1, 1_000, 10, 30, 0, 80),
            StationaryAttackConfig.Default with
            {
                AttackBands = FixedBands(1_000),
                RestEnabled = false
            });

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal([1_000, 40, 30], actions.Leases);
        Assert.Equal(-46, Assert.Single(publisher.States, state => state.Phase == StationaryPhase.MoveGap).RelativeOffsetMs);
        Assert.Equal(-16, Assert.Single(publisher.States, state => state.Phase == StationaryPhase.Stabilizing).RelativeOffsetMs);
        Assert.Equal(-16, publisher.States[^1].RelativeOffsetMs);
    }

    [Fact]
    public async Task Reports_structured_telemetry_after_each_actual_movement_commit()
    {
        Guid sessionId = Guid.NewGuid();
        var actions = new RecordingActionSink();
        actions.EnqueueMovementUp(InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 46, releaseLatenessMs: 6));
        actions.EnqueueMovementUp(InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 30, releaseLatenessMs: 0));
        var telemetry = new RecordingMovementTelemetrySink();
        var controller = CreateController(
            actions,
            new RecordingPublisher(),
            new AdvancingScheduler(),
            new SequenceRandomSource(1, 1_000, 10, 30, 0, 80),
            StationaryAttackConfig.Default with
            {
                AttackBands = FixedBands(1_000),
                RestEnabled = false
            },
            telemetry);

        await controller.RunAsync(sessionId, MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal(2, telemetry.Entries.Count);
        Assert.Equal(
            new StationaryMovementTelemetry(
                sessionId,
                1,
                MovementDirection.Left,
                MovementIntent.Unbiased,
                RequestedHoldMs: 40,
                ActualHoldMs: 46,
                ReleaseLatenessMs: 6,
                OffsetBeforeMs: 0,
                OffsetAfterMs: -46,
                MaxLateralMoveMs: 80),
            telemetry.Entries[0]);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0, 0)]
    [InlineData(5_001, 0)]
    [InlineData(40, null)]
    [InlineData(40, -1)]
    public async Task Stops_when_movement_timing_is_invalid(int? actualHoldMs, int? releaseLatenessMs)
    {
        var actions = new RecordingActionSink();
        actions.EnqueueMovementUp(InputActionResult.Ok(
            "KEY_UP_SENT",
            actualHoldMs,
            releaseLatenessMs));
        var publisher = new RecordingPublisher();
        var controller = CreateController(
            actions,
            publisher,
            new AdvancingScheduler(),
            new SequenceRandomSource(1, 1_000, 0),
            StationaryAttackConfig.Default with
            {
                AttackBands = FixedBands(1_000),
                RestEnabled = false
            });

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal("MOVEMENT_TIMING_INVALID", publisher.States[^1].EarlyReleaseReason);
        Assert.DoesNotContain("Down:MoveRight", actions.Events);
        Assert.Equal("ReleaseAll", actions.Events[^1]);
    }

    [Fact]
    public async Task Stops_when_actual_movement_crosses_the_configured_boundary()
    {
        var actions = new RecordingActionSink();
        actions.EnqueueMovementUp(InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 81, releaseLatenessMs: 51));
        var publisher = new RecordingPublisher();
        var controller = CreateController(
            actions,
            publisher,
            new AdvancingScheduler(),
            new SequenceRandomSource(1, 1_000, 0),
            StationaryAttackConfig.Default with
            {
                AttackBands = FixedBands(1_000),
                RestEnabled = false
            });

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal("MOVEMENT_OFFSET_EXCEEDED", publisher.States[^1].EarlyReleaseReason);
        Assert.DoesNotContain("Down:MoveRight", actions.Events);
        Assert.Equal(0, publisher.States[^1].RelativeOffsetMs);
    }

    [Fact]
    public async Task Continues_when_release_is_over_margin_but_actual_offset_remains_safe()
    {
        var actions = new RecordingActionSink();
        actions.EnqueueMovementUp(InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 61, releaseLatenessMs: 21));
        actions.EnqueueMovementUp(InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 50, releaseLatenessMs: 0));
        var publisher = new RecordingPublisher();
        var controller = CreateController(
            actions,
            publisher,
            new AdvancingScheduler(),
            new SequenceRandomSource(1, 1_000, 10, 30, 19, 80),
            StationaryAttackConfig.Default with
            {
                AttackBands = FixedBands(1_000),
                RestEnabled = false
            });

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Contains("Down:MoveRight", actions.Events);
        Assert.Equal(-61, Assert.Single(publisher.States, state => state.Phase == StationaryPhase.MoveGap).RelativeOffsetMs);
        Assert.Equal(-11, Assert.Single(publisher.States, state => state.Phase == StationaryPhase.Stabilizing).RelativeOffsetMs);
    }

    [Fact]
    public async Task Uses_a_safe_recovery_segment_instead_of_stopping_when_the_next_pair_is_unavailable()
    {
        var actions = new RecordingActionSink();
        actions.EnqueueMovementUp(InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 66, releaseLatenessMs: 20));
        actions.EnqueueMovementUp(InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 46, releaseLatenessMs: 0));
        actions.EnqueueMovementUp(InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 46, releaseLatenessMs: 0));
        var publisher = new RecordingPublisher();
        var telemetry = new RecordingMovementTelemetrySink();
        StationaryAttackConfig config = StationaryAttackConfig.Default with
        {
            AttackBands = FixedBands(1_000),
            MaxLateralMoveMs = 80,
            MoveHoldMinMs = 34,
            MoveHoldMaxMs = 46,
            RestEnabled = false
        };
        var controller = CreateController(
            actions,
            publisher,
            new AdvancingScheduler(),
            new MaximumRandomSource(),
            config,
            telemetry);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 2, CancellationToken.None);

        Assert.Equal(2, actions.Events.Count(item => item == "Down:Attack"));
        Assert.Equal(2, actions.Events.Count(item => item == "Down:MoveRight"));
        Assert.Equal(1, actions.Events.Count(item => item == "Down:MoveLeft"));
        Assert.Null(publisher.States[^1].EarlyReleaseReason);
        Assert.Equal(26, publisher.States[^1].RelativeOffsetMs);
        Assert.Equal(MovementIntent.RecoveryTowardCenter, telemetry.Entries[^1].Intent);
    }

    [Fact]
    public async Task Runs_one_complete_cycle_in_strict_key_order()
    {
        var actions = new RecordingActionSink();
        var publisher = new RecordingPublisher();
        var scheduler = new AdvancingScheduler();
        var random = new SequenceRandomSource(16, 27_438, 0, 47, 0, 101);
        StationaryAttackConfig config = TestConfig() with { RestEnabled = false };
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
                StationaryPhase.AttackReleased,
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

    [Theory]
    [InlineData(47, 147)]
    [InlineData(63, 163)]
    public async Task Adds_direction_release_settle_to_the_random_move_gap(
        int sampledGapMs,
        int expectedTotalGapMs)
    {
        var publisher = new RecordingPublisher();
        var random = new SequenceRandomSource(16, 27_438, 0, sampledGapMs, 0, 101);
        var controller = CreateController(
            new RecordingActionSink(),
            publisher,
            new AdvancingScheduler(),
            random,
            TestConfig() with { RestEnabled = false });

        await controller.RunAsync(
            Guid.NewGuid(),
            MovementDirection.Right,
            cycleLimit: 1,
            CancellationToken.None);

        StationaryRhythmState gap = Assert.Single(
            publisher.States,
            state => state.Phase == StationaryPhase.MoveGap);
        Assert.Equal(
            expectedTotalGapMs,
            gap.PhaseDeadlineMonoMs - gap.PhaseStartedMonoMs);
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
            new SequenceRandomSource(16, 20_001, 0, 30, 1, 80),
            TestConfig() with { RestEnabled = false });

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 2, CancellationToken.None);

        Assert.Equal("ReleaseAll", actions.Events[^1]);
        Assert.Equal(1, actions.Events.Count(item => item == "Down:Attack"));
        Assert.Equal("KEY_UP_FAILED", publisher.States[^1].EarlyReleaseReason);
    }

    [Fact]
    public async Task Operator_cancellation_preserves_a_late_key_up_failure()
    {
        using var cancellation = new CancellationTokenSource();
        var actions = new RecordingActionSink(
            failEvent: "Up:Attack",
            failCode: "KEY_LEASE_DEADLINE_MISSED");
        var publisher = new RecordingPublisher();
        var controller = CreateController(
            actions,
            publisher,
            new CancellingScheduler(cancellation),
            new SequenceRandomSource(16, 20_001),
            TestConfig() with { RestEnabled = false });

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, cancellation.Token);

        Assert.Equal(["Down:Attack", "Up:Attack", "ReleaseAll"], actions.Events);
        Assert.Equal("KEY_LEASE_DEADLINE_MISSED", publisher.States[^1].EarlyReleaseReason);
    }

    [Fact]
    public async Task Operator_cancellation_commits_actual_movement_released_during_stop()
    {
        using var cancellation = new CancellationTokenSource();
        var actions = new RecordingActionSink();
        actions.EnqueueMovementUp(InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 17, releaseLatenessMs: 0));
        var publisher = new RecordingPublisher();
        var controller = CreateController(
            actions,
            publisher,
            new CancelOnDelayCallScheduler(cancellation, cancelOnCall: 3),
            new SequenceRandomSource(1, 1, 0),
            StationaryAttackConfig.Default with
            {
                AttackBands = FixedBands(1),
                RestEnabled = false
            });

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, cancellation.Token);

        Assert.Equal(["Down:Attack", "Up:Attack", "Down:MoveLeft", "Up:MoveLeft", "ReleaseAll"], actions.Events);
        Assert.Equal("CANCELLED", publisher.States[^1].EarlyReleaseReason);
        Assert.Equal(-17, publisher.States[^1].RelativeOffsetMs);
    }

    [Fact]
    public async Task Sends_the_sampled_hold_duration_as_the_broker_lease()
    {
        var actions = new RecordingActionSink();
        var controller = CreateController(
            actions,
            new RecordingPublisher(),
            new AdvancingScheduler(),
            new SequenceRandomSource(16, 27_438, 0, 30, 1, 80),
            TestConfig() with { RestEnabled = false });

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal(27_438, actions.Leases[0]);
        Assert.Equal(80, actions.Leases[1]);
        Assert.Equal(81, actions.Leases[2]);
    }

    [Fact]
    public async Task Completes_the_full_movement_transition_before_the_next_attack_cycle()
    {
        var actions = new RecordingActionSink();
        var publisher = new RecordingPublisher();
        var controller = CreateController(
            actions,
            publisher,
            new AdvancingScheduler(),
            new SequenceRandomSource(
                16, 27_438, 0, 30, 1, 80,
                16, 27_438, 0, 30, 1, 80),
            TestConfig() with { RestEnabled = false });

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 2, CancellationToken.None);

        Assert.Equal(
            [
                "AttackHolding",
                "AttackReleased",
                "MoveFirst",
                "MoveGap",
                "MoveSecond",
                "Stabilizing",
                "AttackHolding",
                "AttackReleased",
                "MoveFirst",
                "MoveGap",
                "MoveSecond",
                "Stabilizing",
                "Stopped"
            ],
            publisher.States.Select(state => state.Phase.ToString()));

        StationaryRhythmState attackReleased = publisher.States[1];
        Assert.Equal(100, attackReleased.PhaseDeadlineMonoMs - attackReleased.PhaseStartedMonoMs);

        int secondAttack = actions.Events.FindIndex(1, item => item == "Down:Attack");
        Assert.True(secondAttack > 0);
        Assert.Equal(
            ["Up:Attack", "Down:MoveLeft", "Up:MoveLeft", "Down:MoveRight", "Up:MoveRight"],
            actions.Events[(secondAttack - 5)..secondAttack]);
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
            new FixedConfigProvider(TestConfig() with { RestEnabled = false }),
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
            MaxLateralMoveMs = 200,
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
            1, 1_000, 0, 30, 0, 80,
            1, 2_000, 0, 40, 0, 90);
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
    public async Task Stops_when_hot_reload_shrinks_the_boundary_inside_the_actual_offset()
    {
        StationaryAttackConfig first = FixedAttackConfig(250, 125, 80);
        StationaryAttackConfig reducedBudget = FixedAttackConfig(100, 80, 80);
        var actions = new RecordingActionSink();
        actions.EnqueueMovementUp(InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 200, releaseLatenessMs: 75));
        actions.EnqueueMovementUp(InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 80, releaseLatenessMs: 0));
        var publisher = new RecordingPublisher();
        var random = new SequenceRandomSource(
            1, 1_000, 45, 30, 0, 80,
            1, 1_000);
        var controller = new StationarySessionController(
            actions,
            new AlwaysSafeGate(),
            new AdvancingScheduler(),
            new SequencedConfigProvider(first, reducedBudget),
            new WeightedAttackDurationSampler(random),
            new StationaryMovementPlanner(random),
            new AlwaysAttackTriggerStrategy(),
            random,
            publisher);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 2, CancellationToken.None);

        Assert.Equal("MOVEMENT_OFFSET_EXCEEDED", publisher.States[^1].EarlyReleaseReason);
        Assert.Equal(-120, publisher.States[^1].RelativeOffsetMs);
        Assert.Equal(1, actions.Events.Count(item => item == "Down:Attack"));
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

    private static StationaryAttackConfig TestConfig() => StationaryAttackConfig.Default with
    {
        AttackBands =
        [
            new AttackBand(1_000, 10_000, 5),
            new AttackBand(10_000, 20_000, 10),
            new AttackBand(20_000, 40_000, 60),
            new AttackBand(40_000, 60_000, 25)
        ],
        MaxLateralMoveMs = 250,
        MoveHoldMinMs = 80,
        MoveHoldMaxMs = 125
    };

    private static StationarySessionController CreateController(
        RecordingActionSink actions,
        RecordingPublisher publisher,
        IMonotonicScheduler scheduler,
        IRandomSource random,
        StationaryAttackConfig config,
        IStationaryMovementTelemetrySink? telemetry = null) =>
        new(
            actions,
            new AlwaysSafeGate(),
            scheduler,
            new FixedConfigProvider(config),
            new WeightedAttackDurationSampler(random),
            new StationaryMovementPlanner(random),
            new AlwaysAttackTriggerStrategy(),
            random,
            publisher,
            telemetry);

    private sealed class RecordingMovementTelemetrySink : IStationaryMovementTelemetrySink
    {
        public List<StationaryMovementTelemetry> Entries { get; } = [];

        public Task WriteAsync(StationaryMovementTelemetry telemetry, CancellationToken cancellationToken)
        {
            Entries.Add(telemetry);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingActionSink(
        string? failEvent = null,
        string failCode = "KEY_UP_FAILED") : IStationaryActionSink
    {
        private readonly Dictionary<StationaryInputAction, int> activeLeases = [];
        private readonly Queue<InputActionResult> movementUpResults = [];
        public List<string> Events { get; } = [];
        public List<int> Leases { get; } = [];

        public void EnqueueMovementUp(InputActionResult result) => movementUpResults.Enqueue(result);

        public Task<InputActionResult> KeyDownAsync(StationaryInputAction action, int leaseMs, CancellationToken cancellationToken)
        {
            Leases.Add(leaseMs);
            activeLeases[action] = leaseMs;
            return Record($"Down:{action}");
        }

        public Task<InputActionResult> KeyUpAsync(StationaryInputAction action, CancellationToken cancellationToken)
        {
            string value = $"Up:{action}";
            Events.Add(value);
            if (value == failEvent) return Task.FromResult(InputActionResult.Fail(failCode));
            if (action is StationaryInputAction.MoveLeft or StationaryInputAction.MoveRight)
            {
                if (movementUpResults.Count > 0) return Task.FromResult(movementUpResults.Dequeue());
                int actualHoldMs = activeLeases[action];
                return Task.FromResult(InputActionResult.Ok("OK", actualHoldMs, releaseLatenessMs: 0));
            }
            return Task.FromResult(InputActionResult.Ok("OK"));
        }

        public Task<InputActionResult> ReleaseAllAsync(CancellationToken cancellationToken)
        {
            Events.Add("ReleaseAll");
            return Task.FromResult(InputActionResult.Ok("ALL_KEYS_RELEASED"));
        }

        private Task<InputActionResult> Record(string value)
        {
            Events.Add(value);
            return Task.FromResult(value == failEvent
                ? InputActionResult.Fail(failCode)
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

    private sealed class CancellingScheduler(CancellationTokenSource cancellation) : IMonotonicScheduler
    {
        public long NowMonoMs { get; private set; } = 10_000;
        private bool cancelled;

        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            NowMonoMs += milliseconds;
            if (!cancelled)
            {
                cancelled = true;
                cancellation.Cancel();
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class CancelOnDelayCallScheduler(
        CancellationTokenSource cancellation,
        int cancelOnCall) : IMonotonicScheduler
    {
        private int calls;
        public long NowMonoMs { get; private set; } = 10_000;

        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            NowMonoMs += milliseconds;
            if (++calls == cancelOnCall) cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
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

    private sealed class MaximumRandomSource : IRandomSource
    {
        public int NextInclusive(int minimum, int maximum) => maximum;
    }
}
