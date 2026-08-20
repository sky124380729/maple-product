using Maple.Core.Broker;
using Maple.InputBroker;

namespace Maple.InputBroker.Tests.Broker;

public sealed class BrokerInputSessionTests
{
    [Fact]
    public async Task Accepts_sixty_second_attack_lease_and_rejects_one_millisecond_more()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock();
        var session = CreateSession(sender, clock);
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
        var session = CreateSession(sender);
        session.Arm(Target(), "secret");

        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 10_000));
        await session.HandleAsync(Request(2, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 10_000));

        Assert.Equal(["Down:Ctrl"], sender.Events);
    }

    [Fact]
    public async Task Lease_deadline_starts_after_physical_key_down_succeeds()
    {
        var clock = new FakeClock { NowMonoMs = 100 };
        var sender = new AdvancingKeyDownSender(clock, keyDownElapsedMs: 5);
        var deadlines = new ManualLeaseDeadlineScheduler();
        var session = CreateSession(sender, clock, deadlines: deadlines);
        session.Arm(Target(), "secret");

        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveLeft, "Left", 25));

        Assert.Equal(130, deadlines.CurrentDeadline(BrokerLogicalAction.MoveLeft));
    }

    [Fact]
    public async Task Watchdog_releases_active_keys_after_heartbeat_timeout()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock();
        var session = CreateSession(sender, clock);
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
        var session = CreateSession(sender);
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
        var session = CreateSession(sender, safety: safety);
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
    public async Task Deadline_releases_the_key_without_watchdog_or_disarming_the_target()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock { NowMonoMs = 100 };
        var deadlines = new ManualLeaseDeadlineScheduler();
        var session = CreateSession(sender, clock, deadlines: deadlines);
        session.Arm(Target(), "secret");
        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 25));

        Assert.Equal(125, deadlines.CurrentDeadline(BrokerLogicalAction.Attack));
        clock.NowMonoMs = 125;
        deadlines.TriggerCurrent(BrokerLogicalAction.Attack);
        BrokerResponse keyUp = await session.HandleAsync(
            Request(2, BrokerCommandKind.KeyUp, BrokerLogicalAction.Attack, "Ctrl", 0));
        BrokerResponse move = await session.HandleAsync(
            Request(3, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveLeft, "Left", 100));

        Assert.True(keyUp.Accepted);
        Assert.Equal("KEY_LEASE_EXPIRED", keyUp.Code);
        Assert.True(move.Accepted);
        Assert.Equal(["Down:Ctrl", "Up:Ctrl", "Down:Left"], sender.Events);
    }

    [Fact]
    public async Task Rejects_the_host_acknowledgement_when_a_lease_is_released_late()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock { NowMonoMs = 100 };
        var deadlines = new ManualLeaseDeadlineScheduler();
        var session = CreateSession(sender, clock, deadlines: deadlines);
        session.Arm(Target(), "secret");
        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveLeft, "Left", 25));

        clock.NowMonoMs = 126;
        deadlines.TriggerCurrent(BrokerLogicalAction.MoveLeft);
        BrokerResponse keyUp = await session.HandleAsync(
            Request(2, BrokerCommandKind.KeyUp, BrokerLogicalAction.MoveLeft, "Left", 0));

        Assert.False(keyUp.Accepted);
        Assert.Equal("KEY_LEASE_DEADLINE_MISSED", keyUp.Code);
        Assert.Equal(["Down:Left", "Up:Left"], sender.Events);
    }

    [Fact]
    public async Task Rejects_the_host_acknowledgement_when_deadline_release_fails()
    {
        var sender = new FailingKeyUpSender(failures: 1);
        var clock = new FakeClock { NowMonoMs = 100 };
        var deadlines = new ManualLeaseDeadlineScheduler();
        var session = CreateSession(sender, clock, deadlines: deadlines);
        session.Arm(Target(), "secret");
        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveRight, "Right", 25));

        clock.NowMonoMs = 125;
        deadlines.TriggerCurrent(BrokerLogicalAction.MoveRight);
        BrokerResponse keyUp = await session.HandleAsync(
            Request(2, BrokerCommandKind.KeyUp, BrokerLogicalAction.MoveRight, "Right", 0));

        Assert.False(keyUp.Accepted);
        Assert.Equal("KEY_LEASE_RELEASE_FAILED", keyUp.Code);
        Assert.Equal(["Down:Right", "Up:Right", "Up:Right"], sender.Events);
        Assert.Empty(session.ActiveKeys);
    }

    [Fact]
    public async Task Stale_deadline_generation_cannot_release_a_refreshed_key()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock { NowMonoMs = 100 };
        var deadlines = new ManualLeaseDeadlineScheduler();
        var session = CreateSession(sender, clock, deadlines: deadlines);
        session.Arm(Target(), "secret");
        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 25));
        long firstGeneration = deadlines.CurrentGeneration(BrokerLogicalAction.Attack);

        clock.NowMonoMs = 110;
        await session.HandleAsync(Request(2, BrokerCommandKind.KeyDown, BrokerLogicalAction.Attack, "Ctrl", 25));
        deadlines.Trigger(BrokerLogicalAction.Attack, firstGeneration);

        Assert.Equal(["Down:Ctrl"], sender.Events);
        Assert.Equal(135, deadlines.CurrentDeadline(BrokerLogicalAction.Attack));
    }

    [Fact]
    public async Task Explicit_key_up_cancels_the_current_deadline()
    {
        var sender = new RecordingKeySender();
        var clock = new FakeClock { NowMonoMs = 100 };
        var deadlines = new ManualLeaseDeadlineScheduler();
        var session = CreateSession(sender, clock, deadlines: deadlines);
        session.Arm(Target(), "secret");
        await session.HandleAsync(Request(1, BrokerCommandKind.KeyDown, BrokerLogicalAction.MoveLeft, "Left", 25));
        long generation = deadlines.CurrentGeneration(BrokerLogicalAction.MoveLeft);

        clock.NowMonoMs = 120;
        BrokerResponse keyUp = await session.HandleAsync(
            Request(2, BrokerCommandKind.KeyUp, BrokerLogicalAction.MoveLeft, "Left", 0));
        deadlines.Trigger(BrokerLogicalAction.MoveLeft, generation);

        Assert.True(keyUp.Accepted);
        Assert.False(deadlines.IsScheduled(BrokerLogicalAction.MoveLeft));
        Assert.Equal(["Down:Left", "Up:Left"], sender.Events);
    }

    [Fact]
    public async Task Rejects_and_releases_when_target_identity_is_no_longer_valid()
    {
        var sender = new RecordingKeySender();
        var safety = new MutableTargetSafety();
        var session = CreateSession(sender, safety: safety);
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
        var session = CreateSession(sender);
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
        var session = CreateSession(sender, clock);
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

    private static BrokerTargetIdentity Target() => new(100, 42, @"C:\Games\MapleStory.exe", 123_456);

    private static BrokerInputSession CreateSession(
        IBrokerKeySender sender,
        FakeClock? clock = null,
        IBrokerTargetSafetyGate? safety = null,
        ManualLeaseDeadlineScheduler? deadlines = null) =>
        new(
            sender,
            clock ?? new FakeClock(),
            safety ?? new AlwaysSafeTarget(),
            deadlines ?? new ManualLeaseDeadlineScheduler(),
            heartbeatTimeoutMs: 2_000);

    private static BrokerRequest Request(
        long sequence,
        BrokerCommandKind kind,
        BrokerLogicalAction? action,
        string? key,
        int leaseMs) =>
        new(BrokerProtocol.Version, sequence, Guid.Parse("5d613b51-405b-4dc7-b1e4-aa95ad314c8f"), kind, action, key, leaseMs);

    private sealed class FakeClock : IBrokerClock
    {
        public long NowMonoMs { get; set; }
    }

    private sealed class ManualLeaseDeadlineScheduler : IBrokerLeaseDeadlineScheduler
    {
        private readonly List<ScheduledDeadline> history = [];
        private readonly Dictionary<BrokerLogicalAction, ScheduledDeadline> current = [];

        public void Schedule(
            BrokerLogicalAction action,
            long generation,
            long deadlineMonoMs,
            Action<BrokerLogicalAction, long> onExpired)
        {
            var scheduled = new ScheduledDeadline(action, generation, deadlineMonoMs, onExpired);
            history.Add(scheduled);
            current[action] = scheduled;
        }

        public void Cancel(BrokerLogicalAction action, long generation)
        {
            if (current.TryGetValue(action, out ScheduledDeadline? scheduled) &&
                scheduled.Generation == generation)
                current.Remove(action);
        }

        public void CancelAll() => current.Clear();

        public long CurrentDeadline(BrokerLogicalAction action) => current[action].DeadlineMonoMs;
        public long CurrentGeneration(BrokerLogicalAction action) => current[action].Generation;
        public bool IsScheduled(BrokerLogicalAction action) => current.ContainsKey(action);

        public void TriggerCurrent(BrokerLogicalAction action)
        {
            ScheduledDeadline scheduled = current[action];
            current.Remove(action);
            scheduled.OnExpired(action, scheduled.Generation);
        }

        public void Trigger(BrokerLogicalAction action, long generation)
        {
            ScheduledDeadline scheduled = history.Single(item =>
                item.Action == action && item.Generation == generation);
            scheduled.OnExpired(action, generation);
        }

        public ValueTask DisposeAsync()
        {
            current.Clear();
            return ValueTask.CompletedTask;
        }

        private sealed record ScheduledDeadline(
            BrokerLogicalAction Action,
            long Generation,
            long DeadlineMonoMs,
            Action<BrokerLogicalAction, long> OnExpired);
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

    private sealed class AdvancingKeyDownSender(FakeClock clock, int keyDownElapsedMs) : IBrokerKeySender
    {
        public bool Send(string key, bool isKeyUp)
        {
            if (!isKeyUp) clock.NowMonoMs += keyDownElapsedMs;
            return true;
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
