using System.IO.Pipes;
using Maple.Core.Broker;

namespace Maple.InputBroker;

public sealed class NamedPipeBrokerServer
{
    public async Task RunAsync(
        string pipeName,
        string secret,
        BrokerTargetIdentity target,
        CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync(cancellationToken);

        var handshakeValidator = new BrokerHandshakeValidator(secret);
        BrokerHandshake? handshake = await BrokerWireCodec.ReadAsync<BrokerHandshake>(pipe, cancellationToken);
        if (handshake is null)
        {
            await BrokerWireCodec.WriteAsync(pipe, new BrokerHandshakeResponse(false, "HANDSHAKE_REQUIRED"), cancellationToken);
            return;
        }

        BrokerHandshakeResponse handshakeResult = handshakeValidator.Validate(handshake);
        if (!handshakeResult.Accepted || handshake.Target != target)
        {
            await BrokerWireCodec.WriteAsync(
                pipe,
                handshakeResult.Accepted
                    ? new BrokerHandshakeResponse(false, "TARGET_IDENTITY_MISMATCH")
                    : handshakeResult,
                cancellationToken);
            return;
        }

        await BrokerWireCodec.WriteAsync(pipe, handshakeResult, cancellationToken);
        await using var session = new BrokerInputSession(
            new KeybdEventInputAdapter(),
            new EnvironmentBrokerClock(),
            new ProcessTargetSafetyGate(),
            heartbeatTimeoutMs: 2_000);
        session.Arm(target, secret);
        using var watchdogCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task watchdog = Task.Run(async () =>
        {
            while (!watchdogCancellation.IsCancellationRequested)
            {
                await Task.Delay(250, watchdogCancellation.Token);
                await session.CheckWatchdogAsync();
            }
        }, watchdogCancellation.Token);

        try
        {
            while (!cancellationToken.IsCancellationRequested && pipe.IsConnected)
            {
                BrokerRequest? request = await BrokerWireCodec.ReadAsync<BrokerRequest>(pipe, cancellationToken);
                if (request is null) break;
                BrokerResponse response = await session.HandleAsync(request);
                await BrokerWireCodec.WriteAsync(pipe, response, cancellationToken);
                if (request.Kind == BrokerCommandKind.Close) break;
            }
        }
        finally
        {
            watchdogCancellation.Cancel();
            try { await watchdog; } catch (OperationCanceledException) { }
        }
    }
}

internal sealed class ProcessTargetSafetyGate : IBrokerTargetSafetyGate
{
    public BrokerTargetSafetyResult Evaluate(BrokerTargetIdentity target)
    {
        if (!OperatingSystem.IsWindows()) return BrokerTargetSafetyResult.Rejected("WINDOWS_REQUIRED");
        return ProcessTargetIdentityProbe.Matches(target)
            ? BrokerTargetSafetyResult.Allowed()
            : BrokerTargetSafetyResult.Rejected("WINDOW_IDENTITY_CHANGED");
    }
}

internal static class ProcessTargetIdentityProbe
{
    public static bool Matches(BrokerTargetIdentity target)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(target.ProcessId);
            string path = process.MainModule?.FileName ?? string.Empty;
            long started = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(target.ProcessPath), StringComparison.OrdinalIgnoreCase) &&
                   started == target.ProcessStartedAtUnixMs;
        }
        catch
        {
            return false;
        }
    }
}
