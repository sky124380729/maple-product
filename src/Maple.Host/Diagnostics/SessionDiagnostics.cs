using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maple.Host.Diagnostics;

public sealed record SessionLogEntry(
    DateTimeOffset TimestampUtc,
    Guid SessionId,
    long CycleId,
    string Phase,
    string Event,
    string ResultCode,
    string? TargetIdentity = null,
    long BrokerSequence = 0,
    string? Direction = null,
    string? MovementIntent = null,
    int? RequestedHoldMs = null,
    int? ActualHoldMs = null,
    int? ReleaseLatenessMs = null,
    int? OffsetBeforeMs = null,
    int? OffsetAfterMs = null,
    int? MaxLateralMoveMs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PlannerKind = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? OffsetBeforePx = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? OffsetAfterPx = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? UncertaintyBeforePx = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? UncertaintyAfterPx = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? UsableHalfWidthPx = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? CandidatePixelsPerMs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? LeftSampleCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? RightSampleCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? LeftMedianPixelsPerMs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? RightMedianPixelsPerMs = null)
{
    public static SessionLogEntry Create(
        Guid sessionId,
        long cycleId,
        string phase,
        string @event,
        string resultCode,
        string? targetIdentity = null,
        long brokerSequence = 0) =>
        new(DateTimeOffset.UtcNow, sessionId, cycleId, phase, @event, resultCode, targetIdentity, brokerSequence);
}

public interface ISessionLog
{
    Task WriteAsync(SessionLogEntry entry, CancellationToken cancellationToken);
}

public sealed class JsonLineSessionLog(string path) : ISessionLog, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public async Task WriteAsync(SessionLogEntry entry, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line, cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public void Dispose() => writeLock.Dispose();
}

public sealed record AbnormalTerminationRecord(
    Guid SessionId,
    string Reason,
    DateTimeOffset StoppedAtUtc);

public sealed class LastAbnormalTerminationStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task SaveAsync(AbnormalTerminationRecord record, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (FileStream stream = new(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporaryPath, path, overwrite: true);
    }

    public async Task<AbnormalTerminationRecord?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<AbnormalTerminationRecord>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(path);
        return Task.CompletedTask;
    }
}
