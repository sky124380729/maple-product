using Maple.Core.Broker;
using Maple.InputBroker;

namespace Maple.InputBroker.Tests.Broker;

public sealed class BrokerInputSessionTests
{
    [Fact]
    public async Task Accepts_vertical_actions_and_releases_other_movement_directions()
    {
        var sender = new RecordingKeySender();
        var session = new BrokerInputSession(sender, new FakeClock(), new AlwaysSafeTarget(), 2_000);
        session.Arm(Target(), "secret");

        BrokerResponse left = await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveLeft, "Left", 100));
        BrokerResponse up = await session.HandleAsync(Request(2, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveUp, "Up", 100));

        Assert.True(left.Accepted);
        Assert.True(up.Accepted);
        Assert.Equal(["Down:Left", "Up:Left", "Down:Up"], sender.Events);
        Assert.Equal(["Up"], session.ActiveKeys);
    }

    [Theory]
    [InlineData(BrokerLogicalAction.MoveUp, "Down")]
    [InlineData(BrokerLogicalAction.MoveDown, "Up")]
    public async Task Rejects_vertical_action_with_wrong_key(BrokerLogicalAction action, string key)
    {
        var session = new BrokerInputSession(new RecordingKeySender(), new FakeClock(), new AlwaysSafeTarget(), 2_000);
        session.Arm(Target(), "secret");

        BrokerResponse response = await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, action, key, 100));

        Assert.False(response.Accepted);
        Assert.Equal("INVALID_DURATION", response.Code);
    }

    [Fact]
    public async Task Accepts_sixty_second_attack_lease_and_rejects_one_millisecond_more()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock();
        var session = new BrokerInputSession(sender, clock, new AlwaysSafeTarget(), heartbeatTimeoutMs: 2_000);
        session.Arm(Target(), "secret");

        BrokerResponse accepted = await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 60_000));
        BrokerResponse rejected = await session.HandleAsync(Request(2, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 60_001));

        Assert.True(accepted.Accepted);
        Assert.False(rejected.Accepted);
        Assert.Equal("INVALID_DURATION", rejected.Code);
    }

    [Fact]
    public async Task Duplicate_key_down_refreshes_lease_without_repeating_physical_key_down()
    {
        var sender = new RecordingKeySender();
        var session = new BrokerInputSession(sender, new FakeClock(), new AlwaysSafeTarget(), 2_000);
        session.Arm(Target(), "secret");

        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 10_000));
        await session.HandleAsync(Request(2, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 10_000));

        Assert.Equal(["Down:Ctrl"], sender.Events);
    }

    [Fact]
    public async Task Watchdog_releases_active_keys_after_heartbeat_timeout()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock();
        var session = new BrokerInputSession(sender, clock, new AlwaysSafeTarget(), 2_000);
        session.Arm(Target(), "secret");
        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 10_000));

        clock.NowMonoMs = 2_001;
        await session.CheckWatchdogAsync();

        Assert.Equal(["Down:Ctrl", "Up:Ctrl"], sender.Events);
        Assert.Empty(session.ActiveKeys);
    }

    [Fact]
    public async Task Release_all_is_idempotent()
    {
        var sender = new RecordingKeySender();
        var session = new BrokerInputSession(sender, new FakeClock(), new AlwaysSafeTarget(), 2_000);
        session.Arm(Target(), "secret");
        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveLeft, "Left", 100));

        BrokerResponse first = await session.HandleAsync(Request(2, BrokerCommandKind.ReleaseAll, null, null, 0));
        BrokerResponse second = await session.HandleAsync(Request(3, BrokerCommandKind.ReleaseAll, null, null, 0));

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.Equal(1, sender.Events.Count(item => item == "Up:Left"));
    }

    [Fact]
    public async Task Release_all_bypasses_target_safety_after_focus_is_lost()
    {
        var sender = new RecordingKeySender();
        var safety = new MutableTargetSafety();
        var session = new BrokerInputSession(sender, new FakeClock(), safety, 2_000);
        session.Arm(Target(), "secret");
        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 10_000));
        safety.Valid = false;

        BrokerResponse response = await session.HandleAsync(
            Request(2, BrokerCommandKind.ReleaseAll, null, null, 0));

        Assert.True(response.Accepted);
        Assert.Equal("ALL_KEYS_RELEASED", response.Code);
        Assert.Equal(["Down:Ctrl", "Up:Ctrl"], sender.Events);
        Assert.Empty(session.ActiveKeys);
    }

    [Fact]
    public async Task Expired_action_lease_releases_the_key_without_disarming_the_target()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock();
        var session = new BrokerInputSession(sender, clock, new AlwaysSafeTarget(), 2_000);
        session.Arm(Target(), "secret");
        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 100));

        clock.NowMonoMs = 101;
        await session.CheckWatchdogAsync();
        BrokerResponse keyUp = await session.HandleAsync(
            Request(2, BrokerCommandKind.KeyUp, BrokerLogicalAction.Attack, "Ctrl", 0));
        BrokerResponse move = await session.HandleAsync(
            Request(3, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveLeft, "Left", 100));

        Assert.True(keyUp.Accepted);
        Assert.Equal("KEY_ALREADY_UP", keyUp.Code);
        Assert.True(move.Accepted);
        Assert.Equal(["Down:Ctrl", "Up:Ctrl", "Down:Left"], sender.Events);
    }

    [Fact]
    public async Task Rejects_and_releases_when_target_identity_is_no_longer_valid()
    {
        var sender = new RecordingKeySender();
        var safety = new MutableTargetSafety();
        var session = new BrokerInputSession(sender, new FakeClock(), safety, 2_000);
        session.Arm(Target(), "secret");
        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 10_000));
        safety.Valid = false;

        BrokerResponse response = await session.HandleAsync(
            Request(2, BrokerCommandKind.KeyUp, BrokerLogicalAction.Attack, "Ctrl", 0));

        Assert.False(response.Accepted);
        Assert.Equal("WINDOW_IDENTITY_CHANGED", response.Code);
        Assert.Equal(["Down:Ctrl", "Up:Ctrl"], sender.Events);
    }

    [Fact]
    public async Task Failed_key_release_stays_tracked_until_a_later_release_all_succeeds()
    {
        var sender = new FailingKeyUpSender(failures: 2);
        var session = new BrokerInputSession(sender, new FakeClock(), new AlwaysSafeTarget(), 2_000);
        session.Arm(Target(), "secret");
        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 10_000));

        BrokerResponse keyUp = await session.HandleAsync(
            Request(2, BrokerCommandKind.KeyUp, BrokerLogicalAction.Attack, "Ctrl", 0));
        BrokerResponse releaseAll = await session.HandleAsync(
            Request(3, BrokerCommandKind.ReleaseAll, null, null, 0));

        Assert.False(keyUp.Accepted);
        Assert.True(releaseAll.Accepted);
        Assert.Equal(["Down:Ctrl", "Up:Ctrl", "Up:Ctrl", "Up:Ctrl"], sender.Events);
        Assert.Empty(session.ActiveKeys);
    }

    [Fact]
    public async Task Heartbeat_timeout_disarms_the_session_and_a_late_heartbeat_cannot_revive_it()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock();
        var session = new BrokerInputSession(sender, clock, new AlwaysSafeTarget(), 2_000);
        session.Arm(Target(), "secret");
        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 10_000));

        clock.NowMonoMs = 2_001;
        await session.CheckWatchdogAsync();
        BrokerResponse heartbeat = await session.HandleAsync(Request(2, BrokerCommandKind.Heartbeat, null, null, 0));
        BrokerResponse keyDown = await session.HandleAsync(
            Request(3, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 10_000));
        BrokerResponse release = await session.HandleAsync(Request(4, BrokerCommandKind.ReleaseAll, null, null, 0));

        Assert.False(heartbeat.Accepted);
        Assert.Equal("TARGET_NOT_ARMED", heartbeat.Code);
        Assert.False(keyDown.Accepted);
        Assert.True(release.Accepted);
        Assert.Equal(["Down:Ctrl", "Up:Ctrl"], sender.Events);
    }

    [Fact]
    public async Task Automatic_movement_release_reports_actual_hold_and_lateness()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock();
        var movementLeases = new ManualMovementLeaseScheduler();
        var session = new BrokerInputSession(
            sender,
            clock,
            new AlwaysSafeTarget(),
            movementLeases,
            2_000);
        session.Arm(Target(), "secret");

        await session.HandleAsync(
            Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveLeft, "Left", 40));
        Assert.Equal(40, movementLeases.DeadlineFor(BrokerLogicalAction.MoveLeft));
        clock.NowMonoMs = 46;
        movementLeases.Expire(BrokerLogicalAction.MoveLeft);
        BrokerResponse keyUp = await session.HandleAsync(
            Request(2, BrokerCommandKind.KeyUp, BrokerLogicalAction.MoveLeft, "Left", 0));

        Assert.True(keyUp.Accepted);
        Assert.Equal("KEY_ALREADY_UP", keyUp.Code);
        Assert.Equal(46, keyUp.ActualHoldMs);
        Assert.Equal(6, keyUp.ReleaseLatenessMs);
        Assert.Equal(["Down:Left", "Up:Left"], sender.Events);
    }

    [Fact]
    public async Task Host_key_up_movement_uses_precise_margin_deadline_and_preserves_timing_for_idempotent_release()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock();
        var movementLeases = new ManualMovementLeaseScheduler();
        var session = new BrokerInputSession(
            sender,
            clock,
            new AlwaysSafeTarget(),
            movementLeases,
            2_000);
        session.Arm(Target(), "secret");

        await session.HandleAsync(Request(
            1,
            BrokerCommandKind.KeyDown,
            BrokerLogicalAction.MoveLeft,
            "Left",
            40,
            BrokerMovementReleaseMode.HostKeyUp));

        Assert.Equal([BrokerLogicalAction.MoveLeft], movementLeases.ScheduledActions);
        Assert.Equal(60, movementLeases.DeadlineFor(BrokerLogicalAction.MoveLeft));
        Assert.Equal(["Left"], session.ActiveKeys);

        clock.NowMonoMs = 41;
        await session.CheckWatchdogAsync();

        Assert.Equal(["Left"], session.ActiveKeys);
        Assert.Equal(["Down:Left"], sender.Events);

        clock.NowMonoMs = 60;
        movementLeases.Expire(BrokerLogicalAction.MoveLeft);
        BrokerResponse keyUp = await session.HandleAsync(
            Request(2, BrokerCommandKind.KeyUp, BrokerLogicalAction.MoveLeft, "Left", 0));

        Assert.Empty(session.ActiveKeys);
        Assert.True(keyUp.Accepted);
        Assert.Equal("KEY_ALREADY_UP", keyUp.Code);
        Assert.Equal(60, keyUp.ActualHoldMs);
        Assert.Equal(20, keyUp.ReleaseLatenessMs);
        Assert.Equal(["Down:Left", "Up:Left"], sender.Events);
    }

    [Fact]
    public async Task Movement_timing_starts_after_physical_key_down_succeeds()
    {
        var clock = new FakeClock();
        var sender = new ClockAdvancingKeySender(clock, downDurationMs: 7);
        var movementLeases = new ManualMovementLeaseScheduler();
        var session = new BrokerInputSession(
            sender,
            clock,
            new AlwaysSafeTarget(),
            movementLeases,
            2_000);
        session.Arm(Target(), "secret");

        await session.HandleAsync(
            Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveRight, "Right", 40));
        clock.NowMonoMs = 47;
        movementLeases.Expire(BrokerLogicalAction.MoveRight);
        BrokerResponse keyUp = await session.HandleAsync(
            Request(2, BrokerCommandKind.KeyUp, BrokerLogicalAction.MoveRight, "Right", 0));

        Assert.Equal(40, keyUp.ActualHoldMs);
        Assert.Equal(0, keyUp.ReleaseLatenessMs);
    }

    [Fact]
    public async Task Watchdog_does_not_preempt_the_movement_deadline_scheduler()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock();
        var movementLeases = new ManualMovementLeaseScheduler();
        var session = new BrokerInputSession(
            sender,
            clock,
            new AlwaysSafeTarget(),
            movementLeases,
            2_000);
        session.Arm(Target(), "secret");

        await session.HandleAsync(
            Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveLeft, "Left", 40));
        clock.NowMonoMs = 41;

        await session.CheckWatchdogAsync();

        Assert.Equal(["Left"], session.ActiveKeys);
        Assert.Equal(["Down:Left"], sender.Events);

        clock.NowMonoMs = 46;
        movementLeases.Expire(BrokerLogicalAction.MoveLeft);
        BrokerResponse keyUp = await session.HandleAsync(
            Request(2, BrokerCommandKind.KeyUp, BrokerLogicalAction.MoveLeft, "Left", 0));

        Assert.Equal(46, keyUp.ActualHoldMs);
        Assert.Equal(6, keyUp.ReleaseLatenessMs);
    }

    [Fact]
    public async Task Direction_preemption_preserves_the_released_movements_timing()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock();
        var movementLeases = new ManualMovementLeaseScheduler();
        var session = new BrokerInputSession(
            sender,
            clock,
            new AlwaysSafeTarget(),
            movementLeases,
            2_000);
        session.Arm(Target(), "secret");

        await session.HandleAsync(
            Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveLeft, "Left", 40));
        clock.NowMonoMs = 35;
        await session.HandleAsync(
            Request(2, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveUp, "Up", 40));
        BrokerResponse leftUp = await session.HandleAsync(
            Request(3, BrokerCommandKind.KeyUp, BrokerLogicalAction.MoveLeft, "Left", 0));

        Assert.True(leftUp.Accepted);
        Assert.Equal("KEY_ALREADY_UP", leftUp.Code);
        Assert.Equal(35, leftUp.ActualHoldMs);
        Assert.Equal(0, leftUp.ReleaseLatenessMs);
        Assert.Equal(["Down:Left", "Up:Left", "Down:Up"], sender.Events);
    }

    [Fact]
    public async Task Explicit_movement_release_cancels_deadline_and_reports_actual_hold()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock();
        var movementLeases = new ManualMovementLeaseScheduler();
        var session = new BrokerInputSession(
            sender,
            clock,
            new AlwaysSafeTarget(),
            movementLeases,
            heartbeatTimeoutMs: 2_000);
        session.Arm(Target(), "secret");

        await session.HandleAsync(
            Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveRight, "Right", 40));
        clock.NowMonoMs = 35;
        BrokerResponse keyUp = await session.HandleAsync(
            Request(2, BrokerCommandKind.KeyUp, BrokerLogicalAction.MoveRight, "Right", 0));

        Assert.True(keyUp.Accepted);
        Assert.Equal("KEY_UP_SENT", keyUp.Code);
        Assert.Equal(35, keyUp.ActualHoldMs);
        Assert.Equal(0, keyUp.ReleaseLatenessMs);
        Assert.Empty(movementLeases.ScheduledActions);
        Assert.Equal(["Down:Right", "Up:Right"], sender.Events);
    }

    [Fact]
    public async Task Attack_does_not_register_a_movement_deadline()
    {
        var movementLeases = new ManualMovementLeaseScheduler();
        var session = new BrokerInputSession(
            new RecordingKeySender(),
            new FakeClock(),
            new AlwaysSafeTarget(),
            movementLeases,
            2_000);
        session.Arm(Target(), "secret");

        await session.HandleAsync(
            Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 1_000));

        Assert.Empty(movementLeases.ScheduledActions);
    }

    [Fact]
    public async Task ReleaseAll_makes_an_already_dequeued_movement_callback_harmless()
    {
        var sender = new RecordingKeySender();
        var movementLeases = new CapturingMovementLeaseScheduler();
        var session = new BrokerInputSession(
            sender,
            new FakeClock(),
            new AlwaysSafeTarget(),
            movementLeases,
            2_000);
        session.Arm(Target(), "secret");

        await session.HandleAsync(
            Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveLeft, "Left", 40));
        BrokerResponse release = await session.HandleAsync(
            Request(2, BrokerCommandKind.ReleaseAll, null, null, 0));
        movementLeases.FireCaptured();

        Assert.True(release.Accepted);
        Assert.Empty(session.ActiveKeys);
        Assert.Equal(["Down:Left", "Up:Left"], sender.Events);
    }

    private static BrokerTargetIdentity Target() => new(100, 42, @"C:\Games\MapleStory.exe", 123_456);

    private static BrokerRequest Request(
        long sequence,
        BrokerCommandKind kind,
        BrokerLogicalAction? action,
        string? key,
        int leaseMs,
        BrokerMovementReleaseMode movementReleaseMode = BrokerMovementReleaseMode.BrokerDeadline) =>
        new(
            BrokerProtocol.Version,
            sequence,
            Guid.Parse("5d613b51-405b-4dc7-b1e4-aa95ad314c8f"),
            kind,
            action,
            key,
            leaseMs,
            movementReleaseMode);

    private sealed class FakeClock : IBrokerClock
    {
        public long NowMonoMs { get; set; }
    }

    private sealed class ManualMovementLeaseScheduler : IMovementLeaseScheduler
    {
        private readonly Dictionary<BrokerLogicalAction, Scheduled> scheduled = [];
        public IReadOnlyCollection<BrokerLogicalAction> ScheduledActions => scheduled.Keys;
        public long DeadlineFor(BrokerLogicalAction action) => scheduled[action].DeadlineMonoMs;

        public void Schedule(
            BrokerLogicalAction action,
            long generation,
            long deadlineMonoMs,
            Action<BrokerLogicalAction, long> onExpired) =>
            scheduled[action] = new Scheduled(generation, deadlineMonoMs, onExpired);

        public void Cancel(BrokerLogicalAction action, long generation)
        {
            if (scheduled.TryGetValue(action, out Scheduled? value) && value.Generation == generation)
                scheduled.Remove(action);
        }

        public void CancelAll() => scheduled.Clear();

        public void Expire(BrokerLogicalAction action)
        {
            Scheduled value = scheduled[action];
            scheduled.Remove(action);
            value.OnExpired(action, value.Generation);
        }

        public ValueTask DisposeAsync()
        {
            scheduled.Clear();
            return ValueTask.CompletedTask;
        }

        private sealed record Scheduled(
            long Generation,
            long DeadlineMonoMs,
            Action<BrokerLogicalAction, long> OnExpired);
    }

    private sealed class CapturingMovementLeaseScheduler : IMovementLeaseScheduler
    {
        private BrokerLogicalAction action;
        private long generation;
        private Action<BrokerLogicalAction, long>? callback;

        public void Schedule(
            BrokerLogicalAction scheduledAction,
            long scheduledGeneration,
            long deadlineMonoMs,
            Action<BrokerLogicalAction, long> onExpired)
        {
            action = scheduledAction;
            generation = scheduledGeneration;
            callback = onExpired;
        }

        public void Cancel(BrokerLogicalAction action, long generation) { }
        public void CancelAll() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void FireCaptured() => callback!(action, generation);
    }

    private sealed class RecordingKeySender : IBrokerKeySender
    {
        public List<string> Events { get; } = [];

        public bool Send(string key, bool isKeyUp)
        {
            Events.Add($"{(isKeyUp ? "Up" : "Down")}:{key}");
            return true;
        }
    }

    private sealed class ClockAdvancingKeySender(FakeClock clock, int downDurationMs) : IBrokerKeySender
    {
        public bool Send(string key, bool isKeyUp)
        {
            if (!isKeyUp) clock.NowMonoMs += downDurationMs;
            return true;
        }
    }

    private sealed class FailingKeyUpSender(int failures) : IBrokerKeySender
    {
        private int remainingFailures = failures;
        public List<string> Events { get; } = [];

        public bool Send(string key, bool isKeyUp)
        {
            Events.Add($"{(isKeyUp ? "Up" : "Down")}:{key}");
            return !isKeyUp || remainingFailures-- <= 0;
        }
    }

    private sealed class AlwaysSafeTarget : IBrokerTargetSafetyGate
    {
        public BrokerTargetSafetyResult Evaluate(BrokerTargetIdentity target) => BrokerTargetSafetyResult.Allowed();
    }

    private sealed class MutableTargetSafety : IBrokerTargetSafetyGate
    {
        public bool Valid { get; set; } = true;
        public BrokerTargetSafetyResult Evaluate(BrokerTargetIdentity target) =>
            Valid ? BrokerTargetSafetyResult.Allowed() : BrokerTargetSafetyResult.Rejected("WINDOW_IDENTITY_CHANGED");
    }
}
