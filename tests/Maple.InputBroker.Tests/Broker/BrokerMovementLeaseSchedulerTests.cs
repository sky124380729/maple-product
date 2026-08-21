using Maple.Core.Broker;
using Maple.InputBroker;

namespace Maple.InputBroker.Tests.Broker;

public sealed class BrokerMovementLeaseSchedulerTests
{
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

    private sealed class FakeClock : IBrokerClock
    {
        public long NowMonoMs { get; set; }
    }
}
