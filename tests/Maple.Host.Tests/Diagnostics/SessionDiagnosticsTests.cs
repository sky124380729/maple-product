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
    public async Task Ordinary_entries_omit_unset_visual_fields_without_changing_existing_null_fields()
    {
        string directory = Path.Combine(Path.GetTempPath(), "maple-log-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "session.jsonl");
        using var log = new JsonLineSessionLog(path);

        await log.WriteAsync(
            SessionLogEntry.Create(Guid.NewGuid(), 8, "AttackHolding", "keyDown", "OK"),
            CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(Assert.Single(await File.ReadAllLinesAsync(path)));
        JsonElement root = document.RootElement;
        string[] visualFields =
        [
            "plannerKind",
            "offsetBeforePx",
            "offsetAfterPx",
            "uncertaintyBeforePx",
            "uncertaintyAfterPx",
            "usableHalfWidthPx",
            "candidatePixelsPerMs",
            "leftSampleCount",
            "rightSampleCount",
            "leftMedianPixelsPerMs",
            "rightMedianPixelsPerMs"
        ];
        foreach (string field in visualFields)
            Assert.False(root.TryGetProperty(field, out _), $"Unexpected visual field: {field}");

        Assert.Equal(JsonValueKind.Null, root.GetProperty("targetIdentity").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("direction").ValueKind);
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

    [Fact]
    public async Task Writes_visual_fallback_optional_fields_as_typed_json_fields()
    {
        string directory = Path.Combine(Path.GetTempPath(), "maple-visual-log-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "session.jsonl");
        using var log = new JsonLineSessionLog(path);

        await log.WriteAsync(
            new SessionLogEntry(
                DateTimeOffset.UtcNow,
                Guid.NewGuid(),
                9,
                "VisualFallback",
                "calibrationCompleted",
                "VISUAL_CALIBRATION_ACCEPTED",
                PlannerKind: "Calibrated",
                Direction: "Right",
                MovementIntent: "ReturnTowardCenter",
                RequestedHoldMs: 41,
                ActualHoldMs: 47,
                OffsetBeforeMs: -10,
                OffsetAfterMs: 37,
                MaxLateralMoveMs: 80,
                OffsetBeforePx: -12.5,
                OffsetAfterPx: 4.25,
                UncertaintyBeforePx: 2.0,
                UncertaintyAfterPx: 4.75,
                UsableHalfWidthPx: 118.0,
                CandidatePixelsPerMs: 0.35,
                LeftSampleCount: 3,
                RightSampleCount: 4,
                LeftMedianPixelsPerMs: 0.31,
                RightMedianPixelsPerMs: 0.34),
            CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(Assert.Single(await File.ReadAllLinesAsync(path)));
        JsonElement root = document.RootElement;
        Assert.Equal(JsonValueKind.Number, root.GetProperty("offsetBeforePx").ValueKind);
        Assert.Equal("Calibrated", root.GetProperty("plannerKind").GetString());
        Assert.Equal(-12.5, root.GetProperty("offsetBeforePx").GetDouble());
        Assert.Equal(4.25, root.GetProperty("offsetAfterPx").GetDouble());
        Assert.Equal(2.0, root.GetProperty("uncertaintyBeforePx").GetDouble());
        Assert.Equal(4.75, root.GetProperty("uncertaintyAfterPx").GetDouble());
        Assert.Equal(118.0, root.GetProperty("usableHalfWidthPx").GetDouble());
        Assert.Equal(0.35, root.GetProperty("candidatePixelsPerMs").GetDouble());
        Assert.Equal(3, root.GetProperty("leftSampleCount").GetInt32());
        Assert.Equal(4, root.GetProperty("rightSampleCount").GetInt32());
        Assert.Equal(0.31, root.GetProperty("leftMedianPixelsPerMs").GetDouble());
        Assert.Equal(0.34, root.GetProperty("rightMedianPixelsPerMs").GetDouble());
    }

    [Fact]
    public async Task Visual_fallback_sink_maps_all_telemetry_fields()
    {
        var log = new RecordingSessionLog();
        var sink = new SessionLogVisualFallbackTelemetrySink(log);
        Guid sessionId = Guid.NewGuid();

        await sink.WriteAsync(
            new VisualFallbackTelemetry(
                sessionId,
                12,
                "segmentCompleted",
                "VISUAL_FALLBACK_SEGMENT_COMPLETED",
                "Calibrated",
                MovementDirection.Left,
                MovementIntent.ReturnTowardCenter,
                RequestedHoldMs: 42,
                ActualHoldMs: 48,
                OffsetBeforeMs: 30,
                OffsetAfterMs: -18,
                MaxLateralMoveMs: 80,
                OffsetBeforePx: 16.5,
                OffsetAfterPx: -1.75,
                UncertaintyBeforePx: 3.0,
                UncertaintyAfterPx: 7.5,
                UsableHalfWidthPx: 118.0,
                CandidatePixelsPerMs: 0.38,
                LeftSampleCount: 5,
                RightSampleCount: 6,
                LeftMedianPixelsPerMs: 0.36,
                RightMedianPixelsPerMs: 0.39),
            CancellationToken.None);

        SessionLogEntry entry = Assert.Single(log.Entries);
        Assert.Equal(sessionId, entry.SessionId);
        Assert.Equal(12, entry.CycleId);
        Assert.Equal("VisualFallback", entry.Phase);
        Assert.Equal("segmentCompleted", entry.Event);
        Assert.Equal("VISUAL_FALLBACK_SEGMENT_COMPLETED", entry.ResultCode);
        Assert.Equal("Calibrated", entry.PlannerKind);
        Assert.Equal("Left", entry.Direction);
        Assert.Equal("ReturnTowardCenter", entry.MovementIntent);
        Assert.Equal(42, entry.RequestedHoldMs);
        Assert.Equal(48, entry.ActualHoldMs);
        Assert.Equal(30, entry.OffsetBeforeMs);
        Assert.Equal(-18, entry.OffsetAfterMs);
        Assert.Equal(80, entry.MaxLateralMoveMs);
        Assert.Equal(16.5, entry.OffsetBeforePx);
        Assert.Equal(-1.75, entry.OffsetAfterPx);
        Assert.Equal(3.0, entry.UncertaintyBeforePx);
        Assert.Equal(7.5, entry.UncertaintyAfterPx);
        Assert.Equal(118.0, entry.UsableHalfWidthPx);
        Assert.Equal(0.38, entry.CandidatePixelsPerMs);
        Assert.Equal(5, entry.LeftSampleCount);
        Assert.Equal(6, entry.RightSampleCount);
        Assert.Equal(0.36, entry.LeftMedianPixelsPerMs);
        Assert.Equal(0.39, entry.RightMedianPixelsPerMs);
    }

    [Fact]
    public async Task Visual_fallback_sink_contains_session_log_failures()
    {
        var sink = new SessionLogVisualFallbackTelemetrySink(new ThrowingSessionLog());
        var telemetry = new VisualFallbackTelemetry(
            Guid.NewGuid(),
            1,
            "candidateRejected",
            "VISUAL_FALLBACK_NO_SAFE_CANDIDATE",
            "Calibrated");

        Exception? exception = await Record.ExceptionAsync(
            () => sink.WriteAsync(telemetry, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Null_visual_fallback_sink_completes_without_persistence()
    {
        var telemetry = new VisualFallbackTelemetry(
            Guid.NewGuid(),
            1,
            "candidateRejected",
            "VISUAL_FALLBACK_NO_SAFE_CANDIDATE",
            "Calibrated");

        await new NullVisualFallbackTelemetrySink().WriteAsync(telemetry, CancellationToken.None);
    }

    private sealed class RecordingSessionLog : ISessionLog
    {
        public List<SessionLogEntry> Entries { get; } = [];

        public Task WriteAsync(SessionLogEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSessionLog : ISessionLog
    {
        public Task WriteAsync(SessionLogEntry entry, CancellationToken cancellationToken) =>
            throw new IOException("Session log unavailable.");
    }
}
