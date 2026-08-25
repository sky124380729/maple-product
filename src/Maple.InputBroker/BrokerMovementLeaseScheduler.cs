using Maple.Core.Broker;

namespace Maple.InputBroker;

public sealed class BrokerMovementLeaseScheduler : IMovementLeaseScheduler
{
    private const int ContinuousCheckThresholdMs = BrokerProtocol.StationaryMovementReleaseSafetyMarginMs;

    private readonly IBrokerClock clock;
    private readonly object sync = new();
    private readonly AutoResetEvent scheduleChanged = new(false);
    private readonly Dictionary<BrokerLogicalAction, ScheduledLease> leases = [];
    private readonly Thread worker;
    private bool disposed;

    public BrokerMovementLeaseScheduler(IBrokerClock clock)
    {
        this.clock = clock;
        worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "Maple.MovementLease",
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
        if (!IsMovement(action))
            throw new ArgumentOutOfRangeException(nameof(action));
        ArgumentNullException.ThrowIfNull(onExpired);

        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            leases[action] = new ScheduledLease(action, generation, deadlineMonoMs, onExpired);
        }
        scheduleChanged.Set();
    }

    public void Cancel(BrokerLogicalAction action, long generation)
    {
        lock (sync)
        {
            if (disposed) return;
            if (leases.TryGetValue(action, out ScheduledLease? lease) && lease.Generation == generation)
                leases.Remove(action);
        }
        scheduleChanged.Set();
    }

    public void CancelAll()
    {
        lock (sync)
        {
            if (disposed) return;
            leases.Clear();
        }
        scheduleChanged.Set();
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposed) return ValueTask.CompletedTask;
            disposed = true;
            leases.Clear();
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
            ScheduledLease? due = null;
            int waitMs = Timeout.Infinite;

            lock (sync)
            {
                if (disposed) return;
                if (leases.Count > 0)
                {
                    ScheduledLease earliest = leases.Values.MinBy(item => item.DeadlineMonoMs)!;
                    long remainingMs = earliest.DeadlineMonoMs - clock.NowMonoMs;
                    if (remainingMs <= 0)
                    {
                        leases.Remove(earliest.Action);
                        due = earliest;
                    }
                    else
                    {
                        waitMs = CalculateBlockingWaitMs(remainingMs);
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

    private sealed record ScheduledLease(
        BrokerLogicalAction Action,
        long Generation,
        long DeadlineMonoMs,
        Action<BrokerLogicalAction, long> OnExpired);

    internal static int CalculateBlockingWaitMs(long remainingMs) =>
        remainingMs <= ContinuousCheckThresholdMs
            ? 0
            : (int)Math.Min(remainingMs - ContinuousCheckThresholdMs, int.MaxValue);

    private static bool IsMovement(BrokerLogicalAction action) => action is
        BrokerLogicalAction.MoveLeft or BrokerLogicalAction.MoveRight
        or BrokerLogicalAction.MoveUp or BrokerLogicalAction.MoveDown;
}
