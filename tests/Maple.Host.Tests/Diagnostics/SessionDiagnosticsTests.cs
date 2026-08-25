using System.Text.Json;
using Maple.Core.Movement;
using Maple.Host.Diagnostics;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Diagnostics;

public sealed class SessionDiagnosticsTests
{
    [Fact]
    public async Task Writes_structured_json_lines_with_session_context()
    {
        string directory = Path.Combine(Path.GetTempPath(), "maple-log-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "session.jsonl");
        var log = new JsonLineSessionLog(path);
        Guid sessionId = Guid.NewGuid();

        await log.WriteAsync(
            SessionLogEntry.Create(sessionId, 7, "MoveSecond", "keyUp", "KEY_UP_FAILED"),
            CancellationToken.None);

        string line = Assert.Single(await File.ReadAllLinesAsync(path));
        using JsonDocument document = JsonDocument.Parse(line);
        Assert.Equal(sessionId, document.RootElement.GetProperty("sessionId").GetGuid());
        Assert.Equal(7, document.RootElement.GetProperty("cycleId").GetInt64());
        Assert.Equal("KEY_UP_FAILED", document.RootElement.GetProperty("resultCode").GetString());
    }

    [Fact]
    public async Task Persists_and_clears_last_abnormal_termination()
    {
        string path = Path.Combine(Path.GetTempPath(), "maple-abnormal-tests", Guid.NewGuid().ToString("N"), "last.json");
        var store = new LastAbnormalTerminationStore(path);
        var record = new AbnormalTerminationRecord(Guid.NewGuid(), "BROKER_DISCONNECTED", DateTimeOffset.UtcNow);

        await store.SaveAsync(record, CancellationToken.None);
        Assert.Equal(record, await store.LoadAsync(CancellationToken.None));

        await store.ClearAsync(CancellationToken.None);
        Assert.Null(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Corrupt_abnormal_record_is_ignored()
    {
        string directory = Path.Combine(Path.GetTempPath(), "maple-abnormal-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "last.json");
        await File.WriteAllTextAsync(path, "{invalid");

        Assert.Null(await new LastAbnormalTerminationStore(path).LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Writes_stationary_movement_telemetry_as_typed_json_fields()
    {
        string directory = Path.Combine(Path.GetTempPath(), "maple-movement-log-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "session.jsonl");
        using var log = new JsonLineSessionLog(path);
        var sink = new SessionLogMovementTelemetrySink(log);
        Guid sessionId = Guid.NewGuid();

        await sink.WriteAsync(
            new StationaryMovementTelemetry(
                sessionId,
                7,
                MovementDirection.Left,
                MovementIntent.ReturnTowardCenter,
                RequestedHoldMs: 40,
                ActualHoldMs: 46,
                ReleaseLatenessMs: 6,
                OffsetBeforeMs: -12,
                OffsetAfterMs: -58,
                MaxLateralMoveMs: 80),
            CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(Assert.Single(await File.ReadAllLinesAsync(path)));
        JsonElement root = document.RootElement;
        Assert.Equal("Left", root.GetProperty("direction").GetString());
        Assert.Equal("ReturnTowardCenter", root.GetProperty("movementIntent").GetString());
        Assert.Equal(40, root.GetProperty("requestedHoldMs").GetInt32());
        Assert.Equal(46, root.GetProperty("actualHoldMs").GetInt32());
        Assert.Equal(6, root.GetProperty("releaseLatenessMs").GetInt32());
        Assert.Equal(-12, root.GetProperty("offsetBeforeMs").GetInt32());
        Assert.Equal(-58, root.GetProperty("offsetAfterMs").GetInt32());
        Assert.Equal(80, root.GetProperty("maxLateralMoveMs").GetInt32());
    }
}
