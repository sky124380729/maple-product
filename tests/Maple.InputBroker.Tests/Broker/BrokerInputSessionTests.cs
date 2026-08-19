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

    private static BrokerTargetIdentity Target() => new(100, 42, @"C:\Games\MapleStory.exe", 123_456);

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

    private sealed class RecordingKeySender : IBrokerKeySender
    {
        public List<string> Events { get; } = [];

        public bool Send(string key, bool isKeyUp)
        {
            Events.Add($"{(isKeyUp ? "Up" : "Down")}:{key}");
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
