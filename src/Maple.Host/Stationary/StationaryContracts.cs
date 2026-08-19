using Maple.Core.Configuration;
using Maple.Core.Session;

namespace Maple.Host.Stationary;

public enum StationaryInputAction
{
    Attack,
    MoveLeft,
    MoveRight
}

public sealed record InputActionResult(bool Success, string Code)
{
    public static InputActionResult Ok(string code) => new(true, code);
    public static InputActionResult Fail(string code) => new(false, code);
}

public sealed record SafetyCheckResult(bool Success, string Code)
{
    public static SafetyCheckResult Allowed() => new(true, "SAFETY_ALLOWED");
    public static SafetyCheckResult Rejected(string code) => new(false, code);
}

public interface IStationaryActionSink
{
    Task<InputActionResult> KeyDownAsync(StationaryInputAction action, int leaseMs, CancellationToken cancellationToken);
    Task<InputActionResult> KeyUpAsync(StationaryInputAction action, CancellationToken cancellationToken);
    Task<InputActionResult> ReleaseAllAsync(CancellationToken cancellationToken);
}

public interface IStationarySafetyGate
{
    Task<SafetyCheckResult> CheckAsync(CancellationToken cancellationToken);
}

public interface IMonotonicScheduler
{
    long NowMonoMs { get; }
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken);
}

public interface IStationaryConfigProvider
{
    StationaryAttackConfig GetValidatedSnapshot();
}

public interface IStationaryStatePublisher
{
    void Publish(StationaryRhythmState state);
}
