namespace Maple.Host.Diagnostics;

public sealed record SessionLogView(
    DateTimeOffset TimestampUtc,
    Guid SessionId,
    long CycleId,
    string Phase,
    string Event,
    string ResultCode,
    long BrokerSequence,
    string? Direction,
    int? OffsetAfterMs)
{
    public static SessionLogView From(SessionLogEntry entry) => new(
        entry.TimestampUtc,
        entry.SessionId,
        entry.CycleId,
        entry.Phase,
        entry.Event,
        entry.ResultCode,
        entry.BrokerSequence,
        entry.Direction,
        entry.OffsetAfterMs);
}
