using Maple.Core.Broker;
using Maple.InputBroker;

namespace Maple.InputBroker.Tests.Broker;

public sealed class BrokerLeaseDeadlineSchedulerTests
{
    [Fact]
    public async Task Does_not_dispatch_a_future_deadline_early()
    {
        var clock = new FakeClock { NowMonoMs = 100 };
        await using var scheduler = new BrokerLeaseDeadlineScheduler(clock);
        using var dispatched = new ManualResetEventSlim();

        scheduler.Schedule(BrokerLogicalAction.MoveLeft, 1, 10_000, (_, _) => dispatched.Set());

        Assert.False(dispatched.Wait(30));
        scheduler.Cancel(BrokerLogicalAction.MoveLeft, 1);
    }

    [Fact]
    public async Task Dispatches_a_deadline_that_is_due()
    {
        var clock = new FakeClock { NowMonoMs = 100 };
        await using var scheduler = new BrokerLeaseDeadlineScheduler(clock);
        using var dispatched = new ManualResetEventSlim();
        BrokerLogicalAction? action = null;
        long generation = 0;

        scheduler.Schedule(BrokerLogicalAction.MoveRight, 7, 100, (expiredAction, expiredGeneration) =>
        {
            action = expiredAction;
            generation = expiredGeneration;
            dispatched.Set();
        });

        Assert.True(dispatched.Wait(1_000));
        Assert.Equal(BrokerLogicalAction.MoveRight, action);
        Assert.Equal(7, generation);
    }

    [Fact]
    public async Task Replacing_an_action_discards_the_old_generation()
    {
        var clock = new FakeClock { NowMonoMs = 100 };
        await using var scheduler = new BrokerLeaseDeadlineScheduler(clock);
        using var dispatched = new ManualResetEventSlim();
        var generations = new List<long>();

        scheduler.Schedule(BrokerLogicalAction.Attack, 1, 10_000, (_, value) => generations.Add(value));
        scheduler.Schedule(BrokerLogicalAction.Attack, 2, 100, (_, value) =>
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
