using Maple.Core.Broker;
using Maple.InputBroker;

namespace Maple.InputBroker.Tests.Broker;

public sealed class BrokerMovementLeaseSchedulerTests
{
    [Fact]
    public void Short_movement_lease_never_uses_a_blocking_wait()
    {
        Assert.Equal(0, BrokerMovementLeaseScheduler.CalculateBlockingWaitMs(20));
        Assert.Equal(80, BrokerMovementLeaseScheduler.CalculateBlockingWaitMs(100));
        Assert.Equal(180, BrokerMovementLeaseScheduler.CalculateBlockingWaitMs(200));
    }

    [Fact]
    public async Task Dispatches_a_due_movement_lease()
    {
        var clock = new FakeClock { NowMonoMs = 100 };
        await using var scheduler = new BrokerMovementLeaseScheduler(clock);
        using var dispatched = new ManualResetEventSlim();

        scheduler.Schedule(BrokerLogicalAction.MoveLeft, 7, 100, (action, generation) =>
        {
            Assert.Equal(BrokerLogicalAction.MoveLeft, action);
            Assert.Equal(7, generation);
            dispatched.Set();
        });

        Assert.True(dispatched.Wait(1_000));
    }

    [Fact]
    public async Task Future_movement_lease_is_not_dispatched_before_its_monotonic_deadline()
    {
        var clock = new FakeClock();
        await using var scheduler = new BrokerMovementLeaseScheduler(clock);
        using var dispatched = new ManualResetEventSlim();

        scheduler.Schedule(BrokerLogicalAction.MoveLeft, 9, 50, (_, _) => dispatched.Set());

        Assert.False(dispatched.Wait(10));
        clock.NowMonoMs = 50;
        Assert.True(dispatched.Wait(1_000));
    }

    [Fact]
    public async Task Dispatches_a_due_vertical_movement_lease()
    {
        var clock = new FakeClock { NowMonoMs = 100 };
        await using var scheduler = new BrokerMovementLeaseScheduler(clock);
        using var dispatched = new ManualResetEventSlim();

        scheduler.Schedule(BrokerLogicalAction.MoveUp, 8, 100, (action, generation) =>
        {
            Assert.Equal(BrokerLogicalAction.MoveUp, action);
            Assert.Equal(8, generation);
            dispatched.Set();
        });

        Assert.True(dispatched.Wait(1_000));
    }

    [Fact]
    public async Task Replacing_a_movement_lease_discards_the_old_generation()
    {
        var clock = new FakeClock { NowMonoMs = 100 };
        await using var scheduler = new BrokerMovementLeaseScheduler(clock);
        using var dispatched = new ManualResetEventSlim();
        var generations = new List<long>();

        scheduler.Schedule(BrokerLogicalAction.MoveRight, 1, 10_000, (_, value) => generations.Add(value));
        scheduler.Schedule(BrokerLogicalAction.MoveRight, 2, 100, (_, value) =>
        {
            generations.Add(value);
            dispatched.Set();
        });

        Assert.True(dispatched.Wait(1_000));
        Assert.Equal([2], generations);
    }

    [Fact]
    public async Task Cancel_prevents_the_matching_generation_from_dispatching()
    {
        var clock = new FakeClock();
        await using var scheduler = new BrokerMovementLeaseScheduler(clock);
        using var sentinelDispatched = new ManualResetEventSlim();
        int cancelledDispatches = 0;

        scheduler.Schedule(BrokerLogicalAction.MoveLeft, 1, 10_000, (_, _) => cancelledDispatches++);
        scheduler.Cancel(BrokerLogicalAction.MoveLeft, 1);
        clock.NowMonoMs = 10_000;
        scheduler.Schedule(BrokerLogicalAction.MoveRight, 2, 10_000, (_, _) => sentinelDispatched.Set());

        Assert.True(sentinelDispatched.Wait(1_000));
        Assert.Equal(0, cancelledDispatches);
    }

    [Fact]
    public async Task CancelAll_prevents_every_registered_lease_from_dispatching()
    {
        var clock = new FakeClock();
        await using var scheduler = new BrokerMovementLeaseScheduler(clock);
        using var sentinelDispatched = new ManualResetEventSlim();
        int cancelledDispatches = 0;

        scheduler.Schedule(BrokerLogicalAction.MoveLeft, 1, 10_000, (_, _) => cancelledDispatches++);
        scheduler.Schedule(BrokerLogicalAction.MoveRight, 2, 10_000, (_, _) => cancelledDispatches++);
        scheduler.CancelAll();
        clock.NowMonoMs = 10_000;
        scheduler.Schedule(BrokerLogicalAction.MoveUp, 3, 10_000, (_, _) => sentinelDispatched.Set());

        Assert.True(sentinelDispatched.Wait(1_000));
        Assert.Equal(0, cancelledDispatches);
    }

    private sealed class FakeClock : IBrokerClock
    {
        public long NowMonoMs { get; set; }
    }
}
