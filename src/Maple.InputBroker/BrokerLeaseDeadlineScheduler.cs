using Maple.Core.Broker;

namespace Maple.InputBroker;

public sealed class BrokerLeaseDeadlineScheduler : IBrokerLeaseDeadlineScheduler
{
    private readonly IBrokerClock clock;
    private readonly object sync = new();
    private readonly AutoResetEvent scheduleChanged = new(false);
    private readonly Dictionary<BrokerLogicalAction, ScheduledDeadline> deadlines = [];
    private readonly Thread worker;
    private bool disposed;

    public BrokerLeaseDeadlineScheduler(IBrokerClock clock)
    {
        this.clock = clock;
        worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "Maple.BrokerLeaseDeadline",
            Priority = ThreadPriority.Highest
        };
        worker.Start();
    }

    public void Schedule(
        BrokerLogicalAction action,
        long generation,
        long deadlineMonoMs,
        Action<BrokerLogicalAction, long> onExpired)
    {
        ArgumentNullException.ThrowIfNull(onExpired);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            deadlines[action] = new ScheduledDeadline(action, generation, deadlineMonoMs, onExpired);
        }
        scheduleChanged.Set();
    }

    public void Cancel(BrokerLogicalAction action, long generation)
    {
        lock (sync)
        {
            if (disposed) return;
            if (deadlines.TryGetValue(action, out ScheduledDeadline? deadline) &&
                deadline.Generation == generation)
                deadlines.Remove(action);
        }
        scheduleChanged.Set();
    }

    public void CancelAll()
    {
        lock (sync)
        {
            if (disposed) return;
            deadlines.Clear();
        }
        scheduleChanged.Set();
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposed) return ValueTask.CompletedTask;
            disposed = true;
            deadlines.Clear();
        }
        scheduleChanged.Set();
        worker.Join();
        scheduleChanged.Dispose();
        return ValueTask.CompletedTask;
    }

    private void Run()
    {
        while (true)
        {
            ScheduledDeadline? due = null;
            int waitMs = Timeout.Infinite;
            lock (sync)
            {
                if (disposed) return;
                if (deadlines.Count > 0)
                {
                    ScheduledDeadline earliest = deadlines.Values.MinBy(item => item.DeadlineMonoMs)!;
                    long remainingMs = earliest.DeadlineMonoMs - clock.NowMonoMs;
                    if (remainingMs <= 0)
                    {
                        deadlines.Remove(earliest.Action);
                        due = earliest;
                    }
                    else if (remainingMs > 2)
                    {
                        waitMs = (int)Math.Min(remainingMs - 1, int.MaxValue);
                    }
                    else
                    {
                        waitMs = 0;
                    }
                }
            }

            if (due is not null)
            {
                try { due.OnExpired(due.Action, due.Generation); }
                catch { }
                continue;
            }

            if (waitMs == 0)
            {
                Thread.SpinWait(128);
                continue;
            }
            scheduleChanged.WaitOne(waitMs);
        }
    }

    private sealed record ScheduledDeadline(
        BrokerLogicalAction Action,
        long Generation,
        long DeadlineMonoMs,
        Action<BrokerLogicalAction, long> OnExpired);
}
