using System.Security.Cryptography;
using System.Text;
using Maple.Core.Broker;

namespace Maple.InputBroker;

public sealed class BrokerHandshakeValidator(string expectedSecret)
{
    public BrokerHandshakeResponse Validate(BrokerHandshake handshake)
    {
        if (handshake.ProtocolVersion != BrokerProtocol.Version)
            return new BrokerHandshakeResponse(false, "PROTOCOL_VERSION_MISMATCH");
        if (!FixedTimeEquals(handshake.Secret, expectedSecret))
            return new BrokerHandshakeResponse(false, "HANDSHAKE_SECRET_INVALID");
        if (handshake.SessionId == Guid.Empty || handshake.Target.Hwnd == 0 || handshake.Target.ProcessId <= 0)
            return new BrokerHandshakeResponse(false, "HANDSHAKE_IDENTITY_INVALID");
        return new BrokerHandshakeResponse(true, "HANDSHAKE_ACCEPTED");
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
