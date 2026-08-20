using System.IO.Pipes;
using System.Runtime.Versioning;
using Maple.Core.Broker;

namespace Maple.InputBroker;

[SupportedOSPlatform("windows")]
public sealed class NamedPipeBrokerServer
{
    public async Task RunAsync(
        string pipeName,
        string secret,
        BrokerTargetIdentity target,
        CancellationToken cancellationToken)
    {
        await using var pipe = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            BrokerPipeSecurity.CreateForCurrentUser());
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
        var clock = new EnvironmentBrokerClock();
        await using var session = new BrokerInputSession(
            new KeybdEventInputAdapter(),
            clock,
            new ProcessTargetSafetyGate(),
            new BrokerLeaseDeadlineScheduler(clock),
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
        if (!ProcessTargetIdentityProbe.Matches(target))
            return BrokerTargetSafetyResult.Rejected("WINDOW_IDENTITY_CHANGED");
        if (ProcessTargetIdentityProbe.IsMinimized(target.Hwnd))
            return BrokerTargetSafetyResult.Rejected("WINDOW_MINIMIZED");
        return ProcessTargetIdentityProbe.IsForeground(target)
            ? BrokerTargetSafetyResult.Allowed()
            : BrokerTargetSafetyResult.Rejected($"FOCUS_LOST:foreground={ProcessTargetIdentityProbe.ForegroundWindow()}");
    }
}

internal static class ProcessTargetIdentityProbe
{
    public static bool Matches(BrokerTargetIdentity target)
    {
        try
        {
            IntPtr hwnd = new(target.Hwnd);
            if (!IsWindow(hwnd)) return false;
            GetWindowThreadProcessId(hwnd, out uint hwndProcessId);
            if (hwndProcessId != target.ProcessId) return false;
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(target.ProcessId);
            string path = ReadExecutablePath(target.ProcessId);
            long started = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(target.ProcessPath), StringComparison.OrdinalIgnoreCase) &&
                   started == target.ProcessStartedAtUnixMs;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadExecutablePath(int processId)
    {
        const uint processQueryLimitedInformation = 0x1000;
        IntPtr process = OpenProcess(processQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero) return string.Empty;
        try
        {
            var path = new System.Text.StringBuilder(32_768);
            int length = path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref length)
                ? Path.GetFullPath(path.ToString())
                : string.Empty;
        }
        finally { CloseHandle(process); }
    }

    public static bool IsForeground(BrokerTargetIdentity target) =>
        RootWindow(GetForegroundWindow()).ToInt64() == target.Hwnd;

    public static long ForegroundWindow() => RootWindow(GetForegroundWindow()).ToInt64();
    public static bool IsMinimized(long hwnd) => IsIconic(new IntPtr(hwnd));

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    private static IntPtr RootWindow(IntPtr hwnd) => GetAncestor(hwnd, 2) is var root && root != IntPtr.Zero ? root : hwnd;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process,
        int flags,
        System.Text.StringBuilder executableName,
        ref int size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
