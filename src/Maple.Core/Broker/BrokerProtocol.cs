namespace Maple.Core.Broker;

public static class BrokerProtocol
{
    public const int Version = 2;
}

public enum BrokerCommandKind
{
    Heartbeat,
    KeyDown,
    KeyUp,
    ReleaseAll,
    Close
}

public enum BrokerLogicalAction
{
    Attack,
    MoveLeft,
    MoveRight
}

public sealed record BrokerTargetIdentity(long Hwnd, int ProcessId, string ProcessPath, long ProcessStartedAtUnixMs);

public sealed record BrokerRequest(
    int ProtocolVersion,
    long Sequence,
    Guid SessionId,
    BrokerCommandKind Kind,
    BrokerLogicalAction? Action,
    string? Key,
    int LeaseMs);

public sealed record BrokerResponse(
    int ProtocolVersion,
    long Sequence,
    bool Accepted,
    string Code,
    int? ActualHoldMs = null,
    int? ReleaseLatenessMs = null);

public sealed record BrokerHandshake(
    int ProtocolVersion,
    string Secret,
    Guid SessionId,
    BrokerTargetIdentity Target);

public sealed record BrokerHandshakeResponse(bool Accepted, string Code);
