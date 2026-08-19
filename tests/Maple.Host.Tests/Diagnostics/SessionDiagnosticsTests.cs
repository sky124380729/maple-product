using System.Text.Json;
using Maple.Host.Diagnostics;

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
}
