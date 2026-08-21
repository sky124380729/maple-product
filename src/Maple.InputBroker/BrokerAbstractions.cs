using Maple.Core.Broker;

namespace Maple.InputBroker;

public interface IBrokerClock
{
    long NowMonoMs { get; }
}

public sealed class EnvironmentBrokerClock : IBrokerClock
{
    public long NowMonoMs => Environment.TickCount64;
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
