using System.Text.Json;
using Maple.Host.Diagnostics;

namespace Maple.Host.Tests.Diagnostics;

public sealed class SessionLogReaderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Returns_the_latest_valid_rows_in_chronological_order()
    {
        string directory = Path.Combine(Path.GetTempPath(), "maple-log-reader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "sessions.jsonl");
        Guid sessionId = Guid.NewGuid();
        List<string> lines = Enumerable.Range(1, 205)
            .Select(cycle => JsonSerializer.Serialize(
                SessionLogEntry.Create(sessionId, cycle, "AttackHolding", "phase", "OK"),
                JsonOptions))
            .ToList();
        lines.Insert(150, "{malformed");
        await File.WriteAllLinesAsync(path, lines);

        IReadOnlyList<SessionLogEntry> result = await new SessionLogReader(path)
            .ReadLatestAsync(200, CancellationToken.None);

        Assert.Equal(200, result.Count);
        Assert.Equal(6, result[0].CycleId);
        Assert.Equal(205, result[^1].CycleId);
        Assert.True(result.Zip(result.Skip(1)).All(pair => pair.First.CycleId < pair.Second.CycleId));
    }

    [Fact]
    public async Task Missing_log_returns_an_empty_collection()
    {
        string path = Path.Combine(Path.GetTempPath(), "maple-log-reader-tests", Guid.NewGuid().ToString("N"), "missing.jsonl");

        IReadOnlyList<SessionLogEntry> result = await new SessionLogReader(path)
            .ReadLatestAsync(200, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Reads_legacy_rows_without_visual_telemetry_fields()
    {
        string directory = Path.Combine(Path.GetTempPath(), "maple-log-reader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "sessions.jsonl");
        Guid sessionId = Guid.NewGuid();
        string legacyRow = $$"""
            {"timestampUtc":"2026-08-26T00:00:00Z","sessionId":"{{sessionId}}","cycleId":4,"phase":"AttackHolding","event":"keyDown","resultCode":"OK"}
            """;
        await File.WriteAllTextAsync(path, legacyRow + Environment.NewLine);

        SessionLogEntry entry = Assert.Single(await new SessionLogReader(path)
            .ReadLatestAsync(200, CancellationToken.None));

        Assert.Equal(sessionId, entry.SessionId);
        Assert.Equal(4, entry.CycleId);
        Assert.Null(entry.PlannerKind);
        Assert.Null(entry.OffsetBeforePx);
        Assert.Null(entry.RightMedianPixelsPerMs);
    }

    [Fact]
    public void Bridge_view_excludes_target_identity_and_unneeded_diagnostic_fields()
    {
        SessionLogEntry source = new(
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            12,
            "MoveSecond",
            "keyUp",
            "OK",
            "pid=42;path=C:\\Games\\Maple.exe",
            7,
            "Right",
            "Random",
            40,
            42,
            2,
            -5,
            37,
            80);

        string json = JsonSerializer.Serialize(SessionLogView.From(source), JsonOptions);

        Assert.DoesNotContain("targetIdentity", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Maple.exe", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("movementIntent", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"offsetAfterMs\":37", json, StringComparison.Ordinal);
    }
}
