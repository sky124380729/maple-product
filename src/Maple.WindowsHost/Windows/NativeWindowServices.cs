using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Maple.Host.Windows;

namespace Maple.WindowsHost.Windows;

public sealed class NativeWindowLocator : IWindowLocator
{
    public Task<IReadOnlyList<WindowIdentity>> FindRunningMapleClientsAsync(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromResult<IReadOnlyList<WindowIdentity>>([]);
        var matches = new List<WindowIdentity>();
        EnumWindows((hwnd, _) =>
        {
            if (cancellationToken.IsCancellationRequested) return false;
            if (!MapleClientWindowFingerprint.Matches(
                    IsWindowVisible(hwnd),
                    ReadWindowText(hwnd),
                    ReadClassName(hwnd))) return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return true;
            try
            {
                using Process process = Process.GetProcessById((int)pid);
                string path = NativeProcessIdentity.ReadExecutablePath((int)pid);
                long started = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
                matches.Add(new WindowIdentity(hwnd.ToInt64(), (int)pid, path, started));
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<WindowIdentity>>(matches);
    }

    private static string ReadWindowText(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0) return string.Empty;
        var value = new StringBuilder(length + 1);
        return GetWindowText(hwnd, value, value.Capacity) > 0 ? value.ToString() : string.Empty;
    }

    private static string ReadClassName(IntPtr hwnd)
    {
        var value = new StringBuilder(256);
        return GetClassName(hwnd, value, value.Capacity) > 0 ? value.ToString() : string.Empty;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder value, int maximum);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder value, int maximum);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}

public sealed class NativeForegroundSession : IForegroundSession
{
    public async Task<ForegroundResult> ActivateAndVerifyAsync(WindowIdentity target, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return ForegroundResult.Rejected("WINDOWS_REQUIRED");
        IntPtr hwnd = new(target.Hwnd);
        if (GetForegroundWindow() == hwnd && !IsIconic(hwnd)) return ForegroundResult.Allowed();
        if (IsIconic(hwnd)) ShowWindow(hwnd, ShowWindowCommand.Restore);

        for (int attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = SetForegroundWindow(hwnd);
            if (GetForegroundWindow() == hwnd && !IsIconic(hwnd)) return ForegroundResult.Allowed();
            await Task.Delay(50, cancellationToken);
        }

        return ForegroundResult.Rejected("FOREGROUND_VERIFY_FAILED");
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, ShowWindowCommand command);

    private enum ShowWindowCommand
    {
        Restore = 9
    }
}

public sealed class NativeWindowIdentityProbe : IWindowIdentityProbe
{
    public Task<WindowProbeResult> ProbeAsync(long hwndValue, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromResult(new WindowProbeResult(null, 0, false, false));
        IntPtr hwnd = new(hwndValue);
        if (!IsWindow(hwnd)) return Task.FromResult(new WindowProbeResult(null, 0, false, false));
        GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using Process process = Process.GetProcessById((int)pid);
            string path = NativeProcessIdentity.ReadExecutablePath((int)pid);
            long started = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
            var identity = new WindowIdentity(hwndValue, (int)pid, path, started);
            return Task.FromResult(new WindowProbeResult(identity, RootWindow(GetForegroundWindow()).ToInt64(), IsIconic(hwnd), true));
        }
        catch
        {
            return Task.FromResult(new WindowProbeResult(null, RootWindow(GetForegroundWindow()).ToInt64(), IsIconic(hwnd), true));
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    private static IntPtr RootWindow(IntPtr hwnd) => GetAncestor(hwnd, 2) is var root && root != IntPtr.Zero ? root : hwnd;
}

internal static class NativeProcessIdentity
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public static string ReadExecutablePath(int processId)
    {
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try
        {
            var path = new System.Text.StringBuilder(32_768);
            int length = path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref length))
                throw new System.ComponentModel.Win32Exception();
            return Path.GetFullPath(path.ToString());
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process,
        int flags,
        System.Text.StringBuilder executableName,
        ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
