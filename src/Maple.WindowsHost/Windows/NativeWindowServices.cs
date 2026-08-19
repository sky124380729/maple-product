using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Maple.Host.Windows;

namespace Maple.WindowsHost.Windows;

public sealed class NativeWindowLocator : IWindowLocator
{
    public Task<IReadOnlyList<WindowIdentity>> FindByExecutablePathAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromResult<IReadOnlyList<WindowIdentity>>([]);
        string normalized = Path.GetFullPath(executablePath);
        var matches = new List<WindowIdentity>();
        EnumWindows((hwnd, _) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return true;
            try
            {
                using Process process = Process.GetProcessById((int)pid);
                string path = process.MainModule?.FileName ?? string.Empty;
                if (string.Equals(Path.GetFullPath(path), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    long started = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
                    matches.Add(new WindowIdentity(hwnd.ToInt64(), (int)pid, path, started));
                }
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        return Task.FromResult<IReadOnlyList<WindowIdentity>>(matches);
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}

public sealed class NativeForegroundSession : IForegroundSession
{
    public Task<ForegroundResult> ActivateAndVerifyAsync(WindowIdentity target, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromResult(ForegroundResult.Rejected("WINDOWS_REQUIRED"));
        IntPtr hwnd = new(target.Hwnd);
        if (IsIconic(hwnd)) ShowWindow(hwnd, ShowWindowCommand.Restore);
        if (!SetForegroundWindow(hwnd)) return Task.FromResult(ForegroundResult.Rejected("FOREGROUND_SWITCH_FAILED"));
        return Task.FromResult(GetForegroundWindow() == hwnd && !IsIconic(hwnd)
            ? ForegroundResult.Allowed()
            : ForegroundResult.Rejected("FOREGROUND_VERIFY_FAILED"));
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
            string path = process.MainModule?.FileName ?? string.Empty;
            long started = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
            var identity = new WindowIdentity(hwndValue, (int)pid, path, started);
            return Task.FromResult(new WindowProbeResult(identity, GetForegroundWindow().ToInt64(), IsIconic(hwnd), true));
        }
        catch
        {
            return Task.FromResult(new WindowProbeResult(null, GetForegroundWindow().ToInt64(), IsIconic(hwnd), true));
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);
}
