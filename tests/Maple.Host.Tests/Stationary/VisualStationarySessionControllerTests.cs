using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;
using Maple.Core.Session;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class VisualStationarySessionControllerTests
{
    [Fact]
    public async Task Untrusted_identity_continues_attack_but_never_sends_direction_input()
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(State(VisualSafetyState.Untrusted, sequence: 3, x: null));
        VisualStationarySessionController controller = Create(actions, observations, new SequenceRandom(1, 1));

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal(["Down:Attack", "Up:Attack", "ReleaseAll"], actions.Events);
        Assert.Empty(observations.WaitedAfterSequences);
    }

    [Fact]
    public async Task Fifteen_seconds_of_identity_loss_without_calibration_uses_ordinary_random_movement()
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(State(VisualSafetyState.Untrusted, sequence: 3, x: null))
        {
            ContinuouslyUntrustedForFallback = true
        };
        VisualStationarySessionController controller = Create(actions, observations, new MinimumRandom());

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal(
            ["Down:Attack", "Up:Attack", "Down:MoveLeft", "Up:MoveLeft", "Down:MoveRight", "Up:MoveRight", "ReleaseAll"],
            actions.Events);
        Assert.Equal(1, observations.MovementTrackingStarts);
        Assert.Equal(1, observations.MovementTrackingEnds);
    }

    [Fact]
    public async Task Fallback_preserves_the_measured_offset_direction_when_the_visual_time_model_exceeds_the_limit()
    {
        var actions = new RecordingActions();
        actions.DirectionActualHolds.Enqueue(160);
        actions.DirectionActualHolds.Enqueue(34);
        actions.DirectionActualHolds.Enqueue(34);
        var observations = new FakeObservations(
            State(VisualSafetyState.Safe, 10, 300),
            State(VisualSafetyState.Safe, 12, 290),
            State(VisualSafetyState.Untrusted, 14, null))
        {
            ContinuouslyUntrustedForFallback = true
        };
        StationaryAttackConfig config = TestConfig() with { MaxLateralMoveMs = 80 };
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            new MinimumRandom(),
            config: config);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 2, CancellationToken.None);

        Assert.Equal(
            ["Down:MoveLeft", "Down:MoveRight", "Down:MoveRight"],
            actions.Events.Where(item => item.StartsWith("Down:Move", StringComparison.Ordinal)).ToArray());
        Assert.Equal(2, actions.Events.Count(item => item == "Down:Attack"));
    }

    [Fact]
    public async Task Fifteen_seconds_of_identity_loss_with_calibration_still_uses_ordinary_random_pair()
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(State(VisualSafetyState.Untrusted, sequence: 3, x: null))
        {
            ContinuouslyUntrustedForFallback = true
        };
        var visualPublisher = new RecordingVisualPublisher();
        VisualFallbackMovementPlanner fallback = CalibratedFallback(new MinimumRandom());
        fallback.ObserveTrustedPosition(0, 48);
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            new MinimumRandom(),
            visualPublisher: visualPublisher,
            fallbackPlanner: fallback);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal(
            ["Down:Attack", "Up:Attack", "Down:MoveLeft", "Up:MoveLeft", "Down:MoveRight", "Up:MoveRight", "ReleaseAll"],
            actions.Events);
        Assert.Contains(visualPublisher.States, state =>
            state.Status == "FallbackContinuous" &&
            state.Code == "VISUAL_FALLBACK_CONTINUOUS" &&
            state.VisualOffsetPx.HasValue);
    }

    [Fact]
    public async Task Stale_or_capture_unavailable_observation_uses_ordinary_fallback_after_timeout()
    {
        foreach ((VisualStationaryObservation state, bool fresh) in new[]
        {
            (State(VisualSafetyState.Untrusted, 3, null), false),
            (State(VisualSafetyState.Untrusted, 3, null, "VISUAL_VIEWPORT_MISMATCH"), true)
        })
        {
            var actions = new RecordingActions();
            var observations = new FakeObservations(state)
            {
                ContinuouslyUntrustedForFallback = true,
                IsFresh = fresh
            };
            VisualFallbackMovementPlanner fallback = CalibratedFallback(new MinimumRandom());
            fallback.ObserveTrustedPosition(0, 48);
            if (!fresh) Assert.True(fallback.TryStartFallback(MovementDirection.Right));
            VisualStationarySessionController controller = Create(
                actions,
                observations,
                new MinimumRandom(),
                fallbackPlanner: fallback);

            await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

            Assert.Contains(actions.Events, item => item.Contains("Move", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Active_fallback_is_invalidated_when_a_brief_reacquisition_resets_the_loss_timer()
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(State(VisualSafetyState.Untrusted, 8, null))
        {
            ContinuouslyUntrustedForFallback = false
        };
        VisualFallbackMovementPlanner fallback = CalibratedFallback(new MinimumRandom());
        fallback.ObserveTrustedPosition(0, 48);
        Assert.True(fallback.TryStartFallback(MovementDirection.Right));
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            new MinimumRandom(),
            fallbackPlanner: fallback);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.DoesNotContain(actions.Events, item => item.Contains("Move", StringComparison.Ordinal));
        Assert.False(fallback.IsFallbackActive);
        Assert.Null(fallback.PredictedOffsetPx);
    }

    [Fact]
    public async Task Trusted_outside_state_never_enters_blind_fallback()
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(State(VisualSafetyState.Outside, 5, 90))
        {
            ContinuouslyUntrustedForFallback = true
        };
        VisualStationarySessionController controller = Create(actions, observations, new MinimumRandom());

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Left, cycleLimit: 1, CancellationToken.None);

        Assert.DoesNotContain(actions.Events, item => item.Contains("Move", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trusted_outside_after_fallback_publishes_outside_instead_of_stale_fallback_status()
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(State(VisualSafetyState.Outside, 9, 190));
        var visualPublisher = new RecordingVisualPublisher();
        VisualFallbackMovementPlanner fallback = CalibratedFallback(new MinimumRandom());
        fallback.ObserveTrustedPosition(0, 48);
        Assert.True(fallback.TryStartFallback(MovementDirection.Right));
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            new MinimumRandom(),
            visualPublisher: visualPublisher,
            fallbackPlanner: fallback);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Contains(visualPublisher.States, state =>
            state.Status == "Outside" && state.Code == "VISUAL_OUTSIDE_FROZEN");
        Assert.DoesNotContain(actions.Events, item => item.Contains("Move", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reacquired_identity_switches_from_fallback_to_visual_on_the_next_complete_cycle()
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(
            State(VisualSafetyState.Untrusted, 3, null),
            State(VisualSafetyState.Safe, 12, 232),
            State(VisualSafetyState.Safe, 14, 251))
        {
            ContinuouslyUntrustedForFallback = true
        };
        int directionReleases = 0;
        actions.DirectionalKeyReleased = () =>
        {
            directionReleases++;
            if (directionReleases != 2) return;
            observations.SetLatest(State(VisualSafetyState.Safe, 10, 300));
            observations.ContinuouslyUntrustedForFallback = false;
        };
        var visualPublisher = new RecordingVisualPublisher();
        VisualFallbackMovementPlanner fallbackPlanner = CalibratedFallback(new MinimumRandom());
        fallbackPlanner.ObserveTrustedPosition(0, 48);
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            new MinimumRandom(),
            visualPublisher: visualPublisher,
            fallbackPlanner: fallbackPlanner);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 2, CancellationToken.None);

        Assert.Equal(2, actions.Events.Count(item => item == "Down:Attack"));
        Assert.Equal(2, actions.Events.Count(item => item == "Down:MoveLeft"));
        Assert.Equal(2, actions.Events.Count(item => item == "Down:MoveRight"));
        Assert.Equal(3, observations.MovementTrackingStarts);
        Assert.Equal(3, observations.MovementTrackingEnds);
        int fallback = visualPublisher.States.FindIndex(state => state.Code == "VISUAL_FALLBACK_CONTINUOUS");
        int recovered = visualPublisher.States.FindIndex(state => state.Code == "VISUAL_FALLBACK_RECOVERED");
        Assert.True(fallback >= 0);
        Assert.True(recovered > fallback);
    }

    [Fact]
    public async Task Left_guard_skips_outward_first_segment_and_executes_random_inward_segment()
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(
            State(VisualSafetyState.GuardLeft, 3, 120),
            State(VisualSafetyState.Safe, 4, 155));
        VisualStationarySessionController controller = Create(actions, observations, new SequenceRandom(1, 1, 41, 1));

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal(
            ["Down:Attack", "Up:Attack", "Down:MoveRight", "Up:MoveRight", "ReleaseAll"],
            actions.Events);
        Assert.Contains(41, actions.Leases);
        Assert.Equal([3], observations.WaitedAfterSequences);
    }

    [Theory]
    [InlineData(240, MovementDirection.Right)]
    [InlineData(360, MovementDirection.Left)]
    public async Task Trusted_offset_outside_center_band_executes_one_random_inward_segment(
        double centerX,
        MovementDirection expectedDirection)
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(
            State(VisualSafetyState.Safe, 10, centerX),
            State(VisualSafetyState.Safe, 12, 300));
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            new MinimumRandom());

        await controller.RunAsync(
            Guid.NewGuid(),
            expectedDirection,
            cycleLimit: 1,
            CancellationToken.None);

        Assert.Equal(1, actions.Events.Count(item => item.StartsWith("Down:Move", StringComparison.Ordinal)));
        Assert.Contains($"Down:Move{expectedDirection}", actions.Events);
    }

    [Fact]
    public async Task Inward_correction_restores_initial_facing_before_the_next_attack()
    {
        using var cancellation = new CancellationTokenSource();
        var actions = new RecordingActions();
        int attackCount = 0;
        actions.AttackKeyPressed = () =>
        {
            attackCount++;
            if (attackCount == 2) cancellation.Cancel();
        };
        var observations = new FakeObservations(
            State(VisualSafetyState.Safe, 10, 360),
            State(VisualSafetyState.Safe, 12, 340),
            State(VisualSafetyState.Safe, 14, 320),
            State(VisualSafetyState.Safe, 16, 330));
        var visualPublisher = new RecordingVisualPublisher();
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            new MinimumRandom(),
            visualPublisher: visualPublisher);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 2, cancellation.Token);

        Assert.Equal(
            ["Down:Attack", "Up:Attack",
             "Down:MoveLeft", "Up:MoveLeft",
             "Down:MoveLeft", "Up:MoveLeft",
             "Down:MoveRight", "Up:MoveRight",
             "Down:Attack", "Up:Attack", "ReleaseAll"],
            actions.Events);
        int pendingIndex = visualPublisher.States.FindIndex(
            state => state.Code == "VISUAL_FACING_RESTORE_PENDING");
        int restoredIndex = visualPublisher.States.FindIndex(
            state => state.Code == "VISUAL_FACING_RESTORED");
        Assert.True(pendingIndex >= 0);
        Assert.True(restoredIndex > pendingIndex);
    }

    [Fact]
    public async Task Safe_state_executes_randomized_pair_and_waits_for_fresh_feedback_after_each_segment()
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(
            State(VisualSafetyState.Safe, 10, 300),
            State(VisualSafetyState.Safe, 12, 280),
            State(VisualSafetyState.Safe, 14, 302));
        var inMotionSequences = new Queue<long>([11, 13]);
        actions.DirectionalKeyReleased = () => observations.SetLatest(
            State(VisualSafetyState.Safe, inMotionSequences.Dequeue(), 290));
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            new SequenceRandom(1, 1, 35, 1, 1, 44, 1));

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal(
            ["Down:Attack", "Up:Attack", "Down:MoveLeft", "Up:MoveLeft", "Down:MoveRight", "Up:MoveRight", "ReleaseAll"],
            actions.Events);
        Assert.Equal([11, 13], observations.WaitedAfterSequences);
        Assert.Equal([1, 35, 44], actions.Leases);
        Assert.Equal(2, observations.MovementTrackingStarts);
        Assert.Equal(2, observations.MovementTrackingEnds);
    }

    [Theory]
    [InlineData(47, 147)]
    [InlineData(63, 163)]
    public async Task Visual_pair_adds_direction_settlement_to_the_random_gap(
        int sampledGapMs,
        int expectedGapMs)
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(
            State(VisualSafetyState.Safe, 10, 300),
            State(VisualSafetyState.Safe, 12, 285),
            State(VisualSafetyState.Safe, 14, 302));
        var scheduler = new ImmediateScheduler();
        var rhythm = new RecordingRhythmPublisher();
        StationaryAttackConfig config = TestConfig() with
        {
            MoveGapMinMs = sampledGapMs,
            MoveGapMaxMs = sampledGapMs
        };
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            new MinimumRandom(),
            scheduler,
            config: config,
            rhythmPublisher: rhythm);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, 1, CancellationToken.None);

        StationaryRhythmState gap = Assert.Single(
            rhythm.States,
            state => state.Phase == StationaryPhase.MoveGap);
        Assert.Equal(expectedGapMs, gap.PhaseDeadlineMonoMs - gap.PhaseStartedMonoMs);
    }

    [Theory]
    [InlineData(47, 147)]
    [InlineData(63, 163)]
    public async Task Visual_fallback_pair_adds_direction_settlement_to_the_random_gap(
        int sampledGapMs,
        int expectedGapMs)
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(State(VisualSafetyState.Untrusted, 10, null))
        {
            ContinuouslyUntrustedForFallback = true
        };
        var scheduler = new ImmediateScheduler();
        var rhythm = new RecordingRhythmPublisher();
        var random = new MinimumRandom();
        VisualFallbackMovementPlanner fallback = CalibratedFallback(random);
        fallback.ObserveTrustedPosition(0, 48);
        StationaryAttackConfig config = TestConfig() with
        {
            MoveGapMinMs = sampledGapMs,
            MoveGapMaxMs = sampledGapMs
        };
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            random,
            scheduler,
            fallbackPlanner: fallback,
            config: config,
            rhythmPublisher: rhythm);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, 1, CancellationToken.None);

        StationaryRhythmState gap = Assert.Single(
            rhythm.States,
            state => state.Phase == StationaryPhase.MoveGap);
        Assert.Equal(expectedGapMs, gap.PhaseDeadlineMonoMs - gap.PhaseStartedMonoMs);
    }

    [Fact]
    public async Task Transient_authorization_cancellation_before_keydown_reacquires_and_completes_pair()
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(
            State(VisualSafetyState.Safe, 10, 300),
            State(VisualSafetyState.Safe, 11, 299),
            State(VisualSafetyState.Safe, 12, 282),
            State(VisualSafetyState.Safe, 13, 301))
        {
            CancelFirstAuthorizationRead = true
        };
        VisualStationarySessionController controller = Create(actions, observations, new MinimumRandom());

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal(
            ["Down:Attack", "Up:Attack", "Down:MoveLeft", "Up:MoveLeft", "Down:MoveRight", "Up:MoveRight", "ReleaseAll"],
            actions.Events);
        Assert.Equal([10, 11, 12], observations.WaitedAfterSequences);
    }

    [Fact]
    public async Task Missed_second_segment_restores_initial_facing_before_the_next_attack()
    {
        using var cancellation = new CancellationTokenSource();
        var actions = new RecordingActions();
        int attackCount = 0;
        actions.AttackKeyPressed = () =>
        {
            attackCount++;
            if (attackCount == 2) cancellation.Cancel();
        };
        var observations = new FakeObservations(
            State(VisualSafetyState.Safe, 10, 300),
            State(VisualSafetyState.Safe, 12, 282),
            State(VisualSafetyState.Safe, 14, 301));
        observations.CancelAuthorizationReads.Add(2);
        observations.TimeoutWaitCalls.UnionWith(Enumerable.Range(2, 8));
        var visualPublisher = new RecordingVisualPublisher();
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            new MinimumRandom(),
            visualPublisher: visualPublisher);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 2, cancellation.Token);

        Assert.Equal(
            ["Down:Attack", "Up:Attack", "Down:MoveLeft", "Up:MoveLeft",
             "Down:MoveRight", "Up:MoveRight", "Down:Attack", "Up:Attack", "ReleaseAll"],
            actions.Events);
        int pendingIndex = visualPublisher.States.FindIndex(
            state => state.Code == "VISUAL_FACING_RESTORE_PENDING");
        int restoredIndex = visualPublisher.States.FindIndex(
            state => state.Code == "VISUAL_FACING_RESTORED");
        Assert.True(pendingIndex >= 0);
        Assert.True(restoredIndex > pendingIndex);
        Assert.All(
            visualPublisher.States[pendingIndex..restoredIndex],
            state => Assert.Equal("FacingRestorePending", state.Status));
    }

    [Fact]
    public async Task Persistent_untrusted_restore_wait_resumes_attack_after_fifteen_seconds()
    {
        using var cancellation = new CancellationTokenSource();
        var actions = new RecordingActions();
        var observations = new FakeObservations(
            State(VisualSafetyState.Safe, 10, 300),
            State(VisualSafetyState.Untrusted, 12, null))
        {
            ContinuouslyUntrustedForFallback = true,
            WaitStarted = call =>
            {
                if (call == 2) cancellation.Cancel();
            }
        };
        actions.DirectionalKeyReleased = observations.RevokeMovement;
        VisualStationarySessionController controller = Create(actions, observations, new MinimumRandom());

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 2, cancellation.Token);

        Assert.Equal(2, actions.Events.Count(item => item == "Down:Attack"));
        Assert.Contains("Down:MoveRight", actions.Events);
        Assert.All(observations.RequestedWaitTimeouts, timeout =>
            Assert.InRange(timeout.TotalMilliseconds, 1, 100));
        Assert.Equal("ReleaseAll", actions.Events[^1]);
    }

    [Fact]
    public async Task Calibrated_fallback_ends_untrusted_facing_restore_wait_and_resumes_attack()
    {
        using var cancellation = new CancellationTokenSource();
        var actions = new RecordingActions();
        var observations = new FakeObservations(
            State(VisualSafetyState.Safe, 10, 300),
            State(VisualSafetyState.Untrusted, 12, null))
        {
            ContinuouslyUntrustedForFallback = true,
            WaitStarted = call =>
            {
                if (call == 2) cancellation.Cancel();
            }
        };
        actions.DirectionalKeyReleased = observations.RevokeMovement;
        VisualFallbackMovementPlanner fallback = CalibratedFallback(new MinimumRandom());
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            new MinimumRandom(),
            fallbackPlanner: fallback);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 2, cancellation.Token);

        Assert.Equal(2, actions.Events.Count(item => item == "Down:Attack"));
        Assert.True(actions.Events.Count(item => item == "Down:MoveLeft") >= 1);
        Assert.Equal("ReleaseAll", actions.Events[^1]);
    }

    [Fact]
    public async Task Outside_state_freezes_movement_instead_of_attempting_automatic_recovery()
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(State(VisualSafetyState.Outside, 5, 90));
        VisualStationarySessionController controller = Create(actions, observations, new SequenceRandom(1, 1));

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Left, cycleLimit: 1, CancellationToken.None);

        Assert.DoesNotContain(actions.Events, item => item.Contains("Move", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stale_trusted_observation_continues_attack_but_never_authorizes_movement()
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(State(VisualSafetyState.Safe, 5, 300))
        {
            IsFresh = false
        };
        VisualStationarySessionController controller = Create(actions, observations, new SequenceRandom(1, 1));

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Left, cycleLimit: 1, CancellationToken.None);

        Assert.Equal(["Down:Attack", "Up:Attack", "ReleaseAll"], actions.Events);
        Assert.Empty(observations.WaitedAfterSequences);
    }

    [Fact]
    public async Task Losing_visual_authority_during_a_direction_hold_releases_it_and_skips_the_next_move()
    {
        using var cancellation = new CancellationTokenSource();
        var actions = new RecordingActions();
        var observations = new FakeObservations(State(VisualSafetyState.Safe, 10, 300))
        {
            WaitStarted = call =>
            {
                if (call == 2) cancellation.Cancel();
            }
        };
        var scheduler = new ImmediateScheduler();
        actions.DirectionalKeyPressed = observations.RevokeMovement;
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            new SequenceRandom(1, 1, 35, 1, 1),
            scheduler);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, cancellation.Token);

        Assert.Equal(
            ["Down:Attack", "Up:Attack", "Down:MoveLeft", "Up:MoveLeft", "ReleaseAll"],
            actions.Events);
        Assert.DoesNotContain(35, scheduler.Delays);
        Assert.DoesNotContain("Down:MoveRight", actions.Events);
    }

    [Fact]
    public async Task Safety_failure_during_visual_feedback_wait_stops_before_the_next_direction()
    {
        var actions = new RecordingActions();
        var observations = new FakeObservations(
            State(VisualSafetyState.Safe, 10, 300),
            State(VisualSafetyState.Safe, 12, 282));
        var safety = new FailOnCheckSafety(4, "FOCUS_LOST");
        VisualStationarySessionController controller = Create(
            actions,
            observations,
            new MinimumRandom(),
            safety: safety);

        await controller.RunAsync(Guid.NewGuid(), MovementDirection.Right, cycleLimit: 1, CancellationToken.None);

        Assert.Equal(
            ["Down:Attack", "Up:Attack", "Down:MoveLeft", "Up:MoveLeft", "ReleaseAll"],
            actions.Events);
        Assert.Equal(4, safety.CheckCount);
        Assert.NotEmpty(observations.RequestedWaitTimeouts);
        Assert.All(observations.RequestedWaitTimeouts, timeout =>
            Assert.InRange(timeout.TotalMilliseconds, 1, 100));
    }

    private static VisualStationarySessionController Create(
        RecordingActions actions,
        FakeObservations observations,
        IRandomSource random,
        ImmediateScheduler? scheduler = null,
        IStationarySafetyGate? safety = null,
        RecordingVisualPublisher? visualPublisher = null,
        VisualFallbackMovementPlanner? fallbackPlanner = null,
        StationaryAttackConfig? config = null,
        RecordingRhythmPublisher? rhythmPublisher = null)
    {
        config ??= TestConfig();
        return new VisualStationarySessionController(
            actions,
            safety ?? new AlwaysSafe(),
            scheduler ?? new ImmediateScheduler(),
            new FixedConfig(config),
            new WeightedAttackDurationSampler(random),
            new VisualStationaryMovementPlanner(random),
            fallbackPlanner ?? new VisualFallbackMovementPlanner(random, platformWidthPx: 276),
            observations,
            random,
            rhythmPublisher ?? new RecordingRhythmPublisher(),
            visualPublisher ?? new RecordingVisualPublisher());
    }

    private static StationaryAttackConfig TestConfig() => StationaryAttackConfig.Default with
    {
        AttackBands =
        [
            new AttackBand(1, 1, 25),
            new AttackBand(1, 1, 25),
            new AttackBand(1, 1, 25),
            new AttackBand(1, 1, 25)
        ],
        MoveHoldMinMs = 34,
        MoveHoldMaxMs = 46,
        MoveGapMinMs = 1,
        MoveGapMaxMs = 1,
        StabilizeMinMs = 1,
        StabilizeMaxMs = 1,
        RestEnabled = false,
        AttackTriggerMode = AttackTriggerMode.VisualSafeContinuous
    };

    private static VisualFallbackMovementPlanner CalibratedFallback(IRandomSource random)
    {
        var planner = new VisualFallbackMovementPlanner(random, platformWidthPx: 276);
        planner.RecordTrustedMovement(MovementDirection.Left, 40, 100, 80);
        planner.RecordTrustedMovement(MovementDirection.Left, 40, 100, 78);
        planner.RecordTrustedMovement(MovementDirection.Right, 40, 100, 120);
        planner.RecordTrustedMovement(MovementDirection.Right, 40, 100, 122);
        return planner;
    }

    private static VisualStationaryObservation State(
        VisualSafetyState state,
        long sequence,
        double? x,
        string? code = null) =>
        new(
            sequence,
            sequence * 10,
            state != VisualSafetyState.Untrusted,
            state == VisualSafetyState.Untrusted ? SelfIdentityStatus.Untrusted : SelfIdentityStatus.Trusted,
            new VisualPlatformState(
                state,
                sequence,
                x,
                0.98,
                32,
                x.HasValue ? (int)(x.Value - 300) : null,
                code ?? (state == VisualSafetyState.Untrusted ? "VISUAL_NAME_SCORE_LOW" : state.ToString())),
            code ?? (state == VisualSafetyState.Untrusted ? "VISUAL_NAME_SCORE_LOW" : state.ToString()));

    private sealed class FakeObservations(params VisualStationaryObservation[] states) : IVisualStationaryObservationSource
    {
        private readonly Queue<VisualStationaryObservation> remaining = new(states.Skip(1));
        private readonly CancellationTokenSource movementAuthorization = new();
        private readonly CancellationToken cancelledAuthorization = CancelledToken();
        private int authorizationReadCount;
        private int waitCallCount;
        public VisualStationaryObservation? Latest { get; private set; } = states[0];
        public bool IsFresh { get; init; } = true;
        public bool ContinuouslyUntrustedForFallback { get; set; }
        public int MovementTrackingStarts { get; private set; }
        public int MovementTrackingEnds { get; private set; }
        public bool CancelFirstAuthorizationRead { get; init; }
        public int? CancelAuthorizationFromRead { get; init; }
        public HashSet<int> CancelAuthorizationReads { get; } = [];
        public HashSet<int> TimeoutWaitCalls { get; } = [];
        public Action<int>? WaitStarted { get; init; }
        public VisualMovementAuthorization? TryAcquireMovementAuthorization(
            MovementDirection direction,
            TimeSpan maximumAge)
        {
            int read = ++authorizationReadCount;
            VisualStationaryObservation? current = Latest;
            if (!IsFresh || current is not { IdentityTrusted: true } ||
                !IsDirectionAllowed(current.Platform.State, direction))
                return null;
            CancellationToken token = CancelFirstAuthorizationRead && read == 1 ||
                CancelAuthorizationFromRead.HasValue && read >= CancelAuthorizationFromRead.Value ||
                CancelAuthorizationReads.Contains(read)
                    ? cancelledAuthorization
                    : movementAuthorization.Token;
            return new VisualMovementAuthorization(current, token);
        }
        public List<long> WaitedAfterSequences { get; } = [];
        public List<TimeSpan> RequestedWaitTimeouts { get; } = [];

        public bool IsLatestFresh(TimeSpan maximumAge) => IsFresh;

        public bool IsContinuouslyUntrustedFor(TimeSpan duration) =>
            ContinuouslyUntrustedForFallback;

        public void BeginMovementTracking(MovementDirection direction) => MovementTrackingStarts++;

        public void EndMovementTracking() => MovementTrackingEnds++;

        public void SetLatest(VisualStationaryObservation observation) => Latest = observation;

        public void RevokeMovement()
        {
            Latest = State(VisualSafetyState.Untrusted, (Latest?.FrameSequence ?? 0) + 1, null);
            movementAuthorization.Cancel();
        }

        public Task<VisualStationaryObservation?> WaitForTrustedAfterAsync(long minimumSequence, TimeSpan timeout, CancellationToken cancellationToken)
        {
            int call = ++waitCallCount;
            WaitedAfterSequences.Add(minimumSequence);
            RequestedWaitTimeouts.Add(timeout);
            WaitStarted?.Invoke(call);
            cancellationToken.ThrowIfCancellationRequested();
            if (TimeoutWaitCalls.Contains(call)) return Task.FromResult<VisualStationaryObservation?>(null);
            if (remaining.Count == 0) return Task.FromResult<VisualStationaryObservation?>(null);
            Latest = remaining.Dequeue();
            return Task.FromResult<VisualStationaryObservation?>(Latest);
        }

        public void RecordMovement(double beforeX, double afterX, double jitterPx) { }

        private static bool IsDirectionAllowed(VisualSafetyState state, MovementDirection direction) =>
            state == VisualSafetyState.Safe ||
            state == VisualSafetyState.GuardLeft && direction == MovementDirection.Right ||
            state == VisualSafetyState.GuardRight && direction == MovementDirection.Left;

        private static CancellationToken CancelledToken()
        {
            var source = new CancellationTokenSource();
            source.Cancel();
            return source.Token;
        }
    }

    private sealed class RecordingActions : IStationaryActionSink
    {
        private readonly Dictionary<StationaryInputAction, int> active = [];
        public List<string> Events { get; } = [];
        public List<int> Leases { get; } = [];
        public Action? AttackKeyPressed { get; set; }
        public Action? DirectionalKeyPressed { get; set; }
        public Action? DirectionalKeyReleased { get; set; }
        public Queue<int> DirectionActualHolds { get; } = [];

        public Task<InputActionResult> KeyDownAsync(StationaryInputAction action, int leaseMs, CancellationToken cancellationToken)
        {
            Events.Add("Down:" + action);
            Leases.Add(leaseMs);
            active[action] = leaseMs;
            if (action == StationaryInputAction.Attack) AttackKeyPressed?.Invoke();
            if (action is StationaryInputAction.MoveLeft or StationaryInputAction.MoveRight)
                DirectionalKeyPressed?.Invoke();
            return Task.FromResult(InputActionResult.Ok("OK"));
        }

        public Task<InputActionResult> KeyUpAsync(StationaryInputAction action, CancellationToken cancellationToken)
        {
            Events.Add("Up:" + action);
            if (action is StationaryInputAction.MoveLeft or StationaryInputAction.MoveRight)
                DirectionalKeyReleased?.Invoke();
            return Task.FromResult(action is StationaryInputAction.MoveLeft or StationaryInputAction.MoveRight
                ? InputActionResult.Ok(
                    "OK",
                    DirectionActualHolds.Count > 0 ? DirectionActualHolds.Dequeue() : active[action],
                    0)
                : InputActionResult.Ok("OK"));
        }

        public Task<InputActionResult> ReleaseAllAsync(CancellationToken cancellationToken)
        {
            Events.Add("ReleaseAll");
            return Task.FromResult(InputActionResult.Ok("OK"));
        }
    }

    private sealed class AlwaysSafe : IStationarySafetyGate
    {
        public Task<SafetyCheckResult> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(SafetyCheckResult.Allowed());
    }

    private sealed class FailOnCheckSafety(int failingCheck, string code) : IStationarySafetyGate
    {
        public int CheckCount { get; private set; }

        public Task<SafetyCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckCount++;
            return Task.FromResult(CheckCount == failingCheck
                ? SafetyCheckResult.Rejected(code)
                : SafetyCheckResult.Allowed());
        }
    }

    private sealed class ImmediateScheduler : IMonotonicScheduler
    {
        public long NowMonoMs { get; private set; }
        public List<int> Delays { get; } = [];
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(milliseconds);
            NowMonoMs += milliseconds;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedConfig(StationaryAttackConfig config) : IStationaryConfigProvider
    {
        public StationaryAttackConfig GetValidatedSnapshot() => config;
    }

    private sealed class RecordingRhythmPublisher : IStationaryStatePublisher
    {
        public List<StationaryRhythmState> States { get; } = [];
        public void Publish(StationaryRhythmState state) => States.Add(state);
    }

    private sealed class RecordingVisualPublisher : IVisualStationaryStatePublisher
    {
        public List<VisualStationaryRuntimeState> States { get; } = [];
        public void Publish(VisualStationaryRuntimeState state) => States.Add(state);
    }

    private sealed class SequenceRandom(params int[] values) : IRandomSource
    {
        private readonly Queue<int> remaining = new(values);
        public int NextInclusive(int minimum, int maximum)
        {
            int value = remaining.Dequeue();
            Assert.InRange(value, minimum, maximum);
            return value;
        }
    }

    private sealed class MinimumRandom : IRandomSource
    {
        public int NextInclusive(int minimum, int maximum) => minimum;
    }
}
