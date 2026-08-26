using System.Text;
using System.Text.Json;

namespace Maple.Host.Diagnostics;

public sealed class SessionLogReader(string path)
{
    private const int ReadBufferSize = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SessionLogEntry>> ReadLatestAsync(
        int maximumEntries,
        CancellationToken cancellationToken)
    {
        if (maximumEntries <= 0 || !File.Exists(path)) return [];

        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            ReadBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length == 0) return [];

        long position = stream.Length;
        var chunks = new LinkedList<byte[]>();
        IReadOnlyList<SessionLogEntry> parsed = [];

        while (position > 0 && parsed.Count < maximumEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int bytesToRead = (int)Math.Min(ReadBufferSize, position);
            position -= bytesToRead;
            stream.Position = position;
            byte[] buffer = new byte[bytesToRead];
            await stream.ReadExactlyAsync(buffer, cancellationToken);
            chunks.AddFirst(buffer);
            parsed = Parse(chunks, position == 0);
        }

        return parsed.TakeLast(maximumEntries).ToArray();
    }

    private static IReadOnlyList<SessionLogEntry> Parse(
        LinkedList<byte[]> chunks,
        bool includesFileStart)
    {
        int length = chunks.Sum(chunk => chunk.Length);
        byte[] bytes = new byte[length];
        int offset = 0;
        foreach (byte[] chunk in chunks)
        {
            chunk.CopyTo(bytes, offset);
            offset += chunk.Length;
        }

        string[] lines = Encoding.UTF8.GetString(bytes)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int firstLine = includesFileStart ? 0 : 1;
        var entries = new List<SessionLogEntry>(lines.Length);
        for (int index = firstLine; index < lines.Length; index++)
        {
            try
            {
                SessionLogEntry? entry = JsonSerializer.Deserialize<SessionLogEntry>(lines[index], JsonOptions);
                if (entry is not null) entries.Add(entry);
            }
            catch (JsonException)
            {
            }
        }
        return entries;
    }
}
