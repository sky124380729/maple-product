using Maple.Host.Broker;
using Maple.Host.Windows;
using System.Diagnostics;

namespace Maple.Host.Tests.Broker;

public sealed class BrokerLaunchFailureTests
{
    [Fact]
    public void Broker_process_start_info_does_not_create_or_show_a_console_window()
    {
        ProcessStartInfo startInfo = WindowsBrokerProcessLauncher.CreateStartInfo(
            "Maple.InputBroker.exe",
            "pipe",
            "secret",
            new WindowIdentity(123, 456, "C:\\Maple.exe", 789));

        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
    }

    [Fact]
    public void Maps_pipe_access_denial_without_crashing_the_host()
    {
        string code = WindowsBrokerProcessLauncher.StartupFailureCode(
            new UnauthorizedAccessException("Access denied"));

        Assert.Equal("BROKER_START_FAILED:UnauthorizedAccessException", code);
    }
}
