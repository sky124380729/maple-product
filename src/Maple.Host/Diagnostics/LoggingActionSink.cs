using Maple.Host.Broker;
using Maple.Host.Stationary;
using Maple.Host.Windows;

namespace Maple.Host.Diagnostics;

public sealed class LoggingActionSink(
    IStationaryActionSink inner,
    IBrokerConnection broker,
    WindowIdentity target,
    ISessionLog log) : IStationaryActionSink
{
    private readonly string targetSummary =
        $"hwnd={target.Hwnd};pid={target.ProcessId};path={target.ProcessPath};started={target.ProcessStartedAtUnixMs}";

    public Task<InputActionResult> KeyDownAsync(
        StationaryInputAction action,
        int leaseMs,
        CancellationToken cancellationToken) =>
        ExecuteAsync("keyDown:" + action, () => inner.KeyDownAsync(action, leaseMs, cancellationToken));

    public Task<InputActionResult> KeyUpAsync(
        StationaryInputAction action,
        CancellationToken cancellationToken) =>
        ExecuteAsync("keyUp:" + action, () => inner.KeyUpAsync(action, cancellationToken));

    public Task<InputActionResult> ReleaseAllAsync(CancellationToken cancellationToken) =>
        ExecuteAsync("releaseAll", () => inner.ReleaseAllAsync(cancellationToken));

    private async Task<InputActionResult> ExecuteAsync(
        string action,
        Func<Task<InputActionResult>> execute)
    {
        InputActionResult result = await execute();
        try
        {
            await log.WriteAsync(SessionLogEntry.Create(
                broker.SessionId,
                0,
                "Input",
                action,
                result.Code,
                targetSummary,
                broker.LastSequence), CancellationToken.None);
        }
        catch { }
        return result;
    }
}
