using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Maple.Host.Preview;

namespace Maple.Host.Navigation;

public sealed record MapRecordingOptions(
    string MapName,
    string OutputDirectory,
    int SampleIntervalMs = 200,
    int MaxSamples = 6000,
    TimeSpan? MaxDuration = null)
{
    public TimeSpan EffectiveMaxDuration => MaxDuration ?? TimeSpan.FromMinutes(10);
}

public sealed record MapRecordingStatus(
    bool IsRecording,
    int SampleCount,
    int PlatformCandidateCount,
    int LadderCandidateCount,
    string? StopReason);

public sealed record MapRecordingResult(
    string PackagePath,
    int SampleCount,
    int PlatformCount,
    int LadderCount,
    string StopReason);

public sealed class MapRecorder : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly MapRecordingOptions options;
    private readonly List<MapRecordingObservation> observations = [];
    private readonly List<PlatformTrack> platformTracks = [];
    private readonly List<LadderTrack> ladderTracks = [];
    private bool recording;
    private long startedAtMonoMs;
    private long lastSampleAtMonoMs = long.MinValue;
    private string? stopReason;
    private MapRecordingResult? completedResult;

    public MapRecorder(MapRecordingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.MapName)
            || string.IsNullOrWhiteSpace(options.OutputDirectory)
            || options.SampleIntervalMs < 50
            || options.MaxSamples is < 1 or > 6000
            || options.EffectiveMaxDuration <= TimeSpan.Zero
            || options.EffectiveMaxDuration > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(options), "MAP_RECORDING_OPTIONS_INVALID");
        this.options = options;
    }

    public bool IsRecording => recording;

    public MapRecordingStatus Start(long capturedAtMonoMs)
    {
        if (recording) throw new InvalidOperationException("MAP_RECORDING_ALREADY_ACTIVE");
        if (completedResult is not null) throw new InvalidOperationException("MAP_RECORDING_ALREADY_COMPLETED");
        recording = true;
        startedAtMonoMs = capturedAtMonoMs;
        lastSampleAtMonoMs = long.MinValue;
        stopReason = null;
        return Status();
    }

    public MapRecordingStatus PushFrame(CapturedFrame frame)
    {
        if (!recording) return Status();
        if (frame.CapturedAtMonoMs - startedAtMonoMs > options.EffectiveMaxDuration.TotalMilliseconds)
        {
            recording = false;
            stopReason = "DURATION_LIMIT";
            return Status();
        }
        if (observations.Count >= options.MaxSamples)
        {
            recording = false;
            stopReason = "SAMPLE_LIMIT";
            return Status();
        }
        if (lastSampleAtMonoMs != long.MinValue
            && frame.CapturedAtMonoMs - lastSampleAtMonoMs < options.SampleIntervalMs)
            return Status();

        MapFrameGeometry geometry = MapFrameGeometryDetector.Detect(frame);
        lastSampleAtMonoMs = frame.CapturedAtMonoMs;
        foreach (MapPlatformCandidate candidate in geometry.Platforms) Track(platformTracks, candidate);
        foreach (MapLadderCandidate candidate in geometry.Ladders) Track(ladderTracks, candidate);
        observations.Add(new MapRecordingObservation(
            frame.Sequence,
            frame.CapturedAtMonoMs,
            geometry.Platforms.ToImmutableArray(),
            geometry.Ladders.ToImmutableArray()));
        if (observations.Count >= options.MaxSamples)
        {
            recording = false;
            stopReason = "SAMPLE_LIMIT";
        }
        return Status();
    }

    public async Task<MapRecordingResult> StopAsync(string reason = "OPERATOR_STOPPED", CancellationToken cancellationToken = default)
    {
        if (completedResult is not null) return completedResult;
        recording = false;
        stopReason ??= string.IsNullOrWhiteSpace(reason) ? "OPERATOR_STOPPED" : reason;
        completedResult = await ExportAsync(stopReason, cancellationToken);
        return completedResult;
    }

    public async ValueTask DisposeAsync()
    {
        if (completedResult is null && (recording || observations.Count > 0))
            await StopAsync("DISPOSED");
    }

    private async Task<MapRecordingResult> ExportAsync(string finalReason, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
            string baseName = SanitizeFileName(options.MapName);
            string targetPath = Path.Combine(options.OutputDirectory,
                $"{baseName}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.mapzip");
            string temporaryPath = targetPath + ".tmp";
            ImmutableArray<MapPlatform> platforms = BuildPlatforms();
            ImmutableArray<MapLadder> ladders = BuildLadders(platforms);
            string manifest = JsonSerializer.Serialize(new
            {
                format = "madudu_map_package",
                version = 1,
                map_name = options.MapName,
                exported_at = DateTimeOffset.UtcNow.ToString("O"),
                match_threshold = 0.15,
                monster_color_corr_threshold = 0.5,
                attack_range_pixels = 140,
                attack_range_height_pixels = 70,
                template_file_count = 0,
                templates_anonymized = true,
                recording_sample_count = observations.Count
            }, JsonOptions);
            string map = JsonSerializer.Serialize(new
            {
                name = options.MapName,
                saved_at = DateTimeOffset.UtcNow.ToString("O"),
                platforms = platforms.Select(platform => new
                {
                    id = platform.Id,
                    x_range = new[] { platform.XMin, platform.XMax },
                    y = platform.Y
                }),
                ladders = ladders.Select(ladder => new
                {
                    id = ladder.Id,
                    x = ladder.X,
                    y_range = new[] { ladder.YMin, ladder.YMax },
                    platform_ids = ladder.PlatformIds
                }),
                platform_links = Array.Empty<object>(),
                jump_links = Array.Empty<object>(),
                drop_links = Array.Empty<object>(),
                portal_links = Array.Empty<object>(),
                teleport_links = Array.Empty<object>(),
                station_points = Array.Empty<object>(),
                match_threshold = 0.15,
                monster_color_corr_threshold = 0.5,
                attack_range_pixels = 140,
                attack_range_height_pixels = 70
            }, JsonOptions);

            await using (FileStream output = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteEntry(archive, "manifest.json", manifest);
                WriteEntry(archive, "map.json", map);
                StringBuilder observationsJson = new();
                foreach (MapRecordingObservation observation in observations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    observationsJson.AppendLine(JsonSerializer.Serialize(observation, JsonOptions));
                }
                WriteEntry(archive, "recording/observations.jsonl", observationsJson.ToString());
            }
            File.Move(temporaryPath, targetPath, overwrite: false);
            return new MapRecordingResult(targetPath, observations.Count, platforms.Length, ladders.Length, finalReason);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new MapPackageLoadException("MAP_RECORDING_INVALID:OUTPUT", inner: exception);
        }
    }

    private ImmutableArray<MapPlatform> BuildPlatforms() =>
        platformTracks.Where(track => track.Count >= 3 && (track.XMax - track.XMin) / track.Count >= 0.06)
            .Select((track, index) => new MapPlatform(
                index,
                track.XMin / track.Count,
                track.XMax / track.Count,
                track.Y / track.Count))
            .OrderBy(platform => platform.Y)
            .ThenBy(platform => platform.XMin)
            .Select((platform, index) => platform with { Id = index })
            .ToImmutableArray();

    private ImmutableArray<MapLadder> BuildLadders(ImmutableArray<MapPlatform> platforms) =>
        ladderTracks.Where(track => track.Count >= 3)
            .Select((track, index) =>
            {
                double x = track.X / track.Count;
                double yMin = track.YMin / track.Count;
                double yMax = track.YMax / track.Count;
                ImmutableArray<int> platformIds = platforms
                    .Where(platform => x >= platform.XMin - 0.08 && x <= platform.XMax + 0.08
                        && platform.Y >= yMin - 0.04 && platform.Y <= yMax + 0.04)
                    .Select(platform => platform.Id)
                    .ToImmutableArray();
                return new MapLadder(index, x, yMin, yMax, platformIds);
            })
            .ToImmutableArray();

    private MapRecordingStatus Status() => new(
        recording,
        observations.Count,
        platformTracks.Count(track => track.Count >= 3),
        ladderTracks.Count(track => track.Count >= 3),
        stopReason);

    private static void Track(List<PlatformTrack> tracks, MapPlatformCandidate candidate)
    {
        PlatformTrack? track = tracks.FirstOrDefault(existing =>
            Math.Abs(existing.XMin / existing.Count - candidate.XMin) < 0.025
            && Math.Abs(existing.XMax / existing.Count - candidate.XMax) < 0.025
            && Math.Abs(existing.Y / existing.Count - candidate.Y) < 0.025);
        if (track is null) tracks.Add(new PlatformTrack(candidate.XMin, candidate.XMax, candidate.Y));
        else track.Add(candidate.XMin, candidate.XMax, candidate.Y);
    }

    private static void Track(List<LadderTrack> tracks, MapLadderCandidate candidate)
    {
        LadderTrack? track = tracks.FirstOrDefault(existing =>
            Math.Abs(existing.X / existing.Count - candidate.X) < 0.025
            && Math.Abs(existing.YMin / existing.Count - candidate.YMin) < 0.04
            && Math.Abs(existing.YMax / existing.Count - candidate.YMax) < 0.04);
        if (track is null) tracks.Add(new LadderTrack(candidate.X, candidate.YMin, candidate.YMax));
        else track.Add(candidate.X, candidate.YMin, candidate.YMax);
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string result = string.Concat(name.Select(character => invalid.Contains(character) ? '_' : character));
        return string.IsNullOrWhiteSpace(result) ? "recorded-map" : result;
    }

    private sealed class PlatformTrack(double xMin, double xMax, double y)
    {
        public double XMin = xMin;
        public double XMax = xMax;
        public double Y = y;
        public int Count = 1;
        public void Add(double nextXMin, double nextXMax, double nextY)
        {
            XMin += nextXMin;
            XMax += nextXMax;
            Y += nextY;
            Count++;
        }
    }

    private sealed class LadderTrack(double x, double yMin, double yMax)
    {
        public double X = x;
        public double YMin = yMin;
        public double YMax = yMax;
        public int Count = 1;
        public void Add(double nextX, double nextYMin, double nextYMax)
        {
            X += nextX;
            YMin += nextYMin;
            YMax += nextYMax;
            Count++;
        }
    }
}

public sealed record MapRecordingObservation(
    long Sequence,
    long CapturedAtMonoMs,
    ImmutableArray<MapPlatformCandidate> Platforms,
    ImmutableArray<MapLadderCandidate> Ladders);
