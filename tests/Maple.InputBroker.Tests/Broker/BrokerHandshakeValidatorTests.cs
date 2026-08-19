using Maple.Core.Broker;
using Maple.InputBroker;

namespace Maple.InputBroker.Tests.Broker;

public sealed class BrokerHandshakeValidatorTests
{
    [Fact]
    public void Rejects_wrong_protocol_or_secret()
    {
        var validator = new BrokerHandshakeValidator("expected");
        BrokerTargetIdentity target = new(100, 42, @"C:\Games\MapleStory.exe", 1234);

        Assert.False(validator.Validate(new BrokerHandshake(999, "expected", Guid.NewGuid(), target)).Accepted);
        Assert.False(validator.Validate(new BrokerHandshake(1, "wrong", Guid.NewGuid(), target)).Accepted);
    }

    [Fact]
    public void Accepts_complete_matching_handshake()
    {
        var validator = new BrokerHandshakeValidator("expected");
        BrokerTargetIdentity target = new(100, 42, @"C:\Games\MapleStory.exe", 1234);

        BrokerHandshakeResponse result = validator.Validate(
            new BrokerHandshake(BrokerProtocol.Version, "expected", Guid.NewGuid(), target));

        Assert.True(result.Accepted);
    }
}
