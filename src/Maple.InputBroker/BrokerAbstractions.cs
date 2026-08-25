using Maple.Core.Broker;
using System.Diagnostics;

namespace Maple.InputBroker;

public interface IBrokerClock
{
    long NowMonoMs { get; }
}

public sealed class EnvironmentBrokerClock : IBrokerClock
{
    public long NowMonoMs =>
        (long)(Stopwatch.GetTimestamp() * (1_000d / Stopwatch.Frequency));
}

public interface IMovementLeaseScheduler : IAsyncDisposable
{
    void Schedule(
        BrokerLogicalAction action,
        long generation,
        long deadlineMonoMs,
        Action<BrokerLogicalAction, long> onExpired);

    void Cancel(BrokerLogicalAction action, long generation);
    void CancelAll();
}

internal sealed class NoopMovementLeaseScheduler : IMovementLeaseScheduler
{
    public void Schedule(
        BrokerLogicalAction action,
        long generation,
        long deadlineMonoMs,
        Action<BrokerLogicalAction, long> onExpired)
    {
    }

    public void Cancel(BrokerLogicalAction action, long generation)
    {
    }

    public void CancelAll()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public interface IBrokerKeySender
{
    bool Send(string key, bool isKeyUp);
}

public sealed record BrokerTargetSafetyResult(bool Success, string Code)
{
    public static BrokerTargetSafetyResult Allowed() => new(true, "TARGET_VALID");
    public static BrokerTargetSafetyResult Rejected(string code) => new(false, code);
}

public interface IBrokerTargetSafetyGate
{
    BrokerTargetSafetyResult Evaluate(BrokerTargetIdentity target);
}
