using System.Diagnostics;
using System.Security.Cryptography;
using Maple.Host.Windows;

namespace Maple.Host.Broker;

public sealed class WindowsBrokerProcessLauncher(string brokerExecutablePath) : IBrokerProcessLauncher
{
    public async Task<BrokerLaunchResult> StartAndArmAsync(
        WindowIdentity target,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return BrokerLaunchResult.Failed("WINDOWS_REQUIRED");
        string pipeName = "maple-input-" + Guid.NewGuid().ToString("N");
        string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var startInfo = new ProcessStartInfo
        {
            FileName = brokerExecutablePath,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = string.Join(
                " ",
                "--pipe", Quote(pipeName),
                "--secret", Quote(secret),
                "--hwnd", target.Hwnd,
                "--pid", target.ProcessId,
                "--path", Quote(target.ProcessPath),
                "--started", target.ProcessStartedAtUnixMs)
        };

        Process? brokerProcess = null;
        try
        {
            brokerProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("BROKER_PROCESS_NOT_STARTED");
            NamedPipeBrokerClient connection = await NamedPipeBrokerClient.ConnectAsync(
                pipeName,
                secret,
                target,
                sessionId,
                cancellationToken);
            brokerProcess.Dispose();
            brokerProcess = null;
            return BrokerLaunchResult.Started(connection);
        }
        catch (OperationCanceledException)
        {
            TryTerminate(brokerProcess);
            return BrokerLaunchResult.Failed("BROKER_START_CANCELLED");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or TimeoutException)
        {
            TryTerminate(brokerProcess);
            return BrokerLaunchResult.Failed(StartupFailureCode(exception));
        }
    }

    public static string StartupFailureCode(Exception exception) =>
        "BROKER_START_FAILED:" + exception.GetType().Name;

    private static string Quote(object value) => "\"" + value.ToString()!.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static void TryTerminate(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }
}
