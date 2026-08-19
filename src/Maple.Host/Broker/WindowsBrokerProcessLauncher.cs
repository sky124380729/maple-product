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

        try
        {
            Process.Start(startInfo);
            NamedPipeBrokerClient connection = await NamedPipeBrokerClient.ConnectAsync(
                pipeName,
                secret,
                target,
                sessionId,
                cancellationToken);
            return BrokerLaunchResult.Started(connection);
        }
        catch (OperationCanceledException)
        {
            return BrokerLaunchResult.Failed("BROKER_START_CANCELLED");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return BrokerLaunchResult.Failed("BROKER_START_FAILED:" + exception.GetType().Name);
        }
    }

    private static string Quote(object value) => "\"" + value.ToString()!.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
