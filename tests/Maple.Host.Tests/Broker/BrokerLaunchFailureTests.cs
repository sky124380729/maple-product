using Maple.Host.Broker;

namespace Maple.Host.Tests.Broker;

public sealed class BrokerLaunchFailureTests
{
    [Fact]
    public void Maps_pipe_access_denial_without_crashing_the_host()
    {
        string code = WindowsBrokerProcessLauncher.StartupFailureCode(
            new UnauthorizedAccessException("Access denied"));

        Assert.Equal("BROKER_START_FAILED:UnauthorizedAccessException", code);
    }
}
