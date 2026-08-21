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
    TimeSpan? MaxDuration = null,
    int MaxObservationEntryBytes = 8 * 1024 * 1024,
    int MaxTotalObservationBytes = 48 * 1024 * 1024)
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
    string StopReason,
    bool PlanningReady,
    ImmutableArray<string> QualityReasons);

public sealed class MapRecorder : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions ObservationJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private const int MaxObservationEntries = 510;
    private const long MaximumTrajectoryGapMs = 1000;

    private readonly MapRecordingOptions options;
    private readonly List<MapRecordingObservation> observations = [];
    private readonly List<PlatformTrack> platformTracks = [];
    private readonly List<LadderTrack> ladderTracks = [];
    private bool recording;
    private long startedAtMonoMs;
    private long lastSampleAtMonoMs = long.MinValue;
    private string? stopReason;
    private MapRecordingResult? completedResult;
    private int totalObservationBytes;
    private int currentObservationChunkBytes;
    private int observationChunkCount;
    private bool finalizationAttempted;
    private Exception? finalizationError;
    private int unverifiedConnectorCount;
    private readonly HashSet<long> localEvidenceSequences = [];

    public MapRecorder(MapRecordingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.MapName)
            || options.MapName.Length > 256
            || string.IsNullOrWhiteSpace(options.OutputDirectory)
            || options.SampleIntervalMs < 50
            || options.MaxSamples is < 1 or > 6000
            || options.MaxObservationEntryBytes is < 512 or > 16 * 1024 * 1024
            || options.MaxTotalObservationBytes is < 1024 or > 60 * 1024 * 1024
            || options.EffectiveMaxDuration <= TimeSpan.Zero
            || options.EffectiveMaxDuration > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(options), "MAP_RECORDING_OPTIONS_INVALID");
        this.options = options;
    }

    public bool IsRecording => recording;

    public MapRecordingStatus Start(long capturedAtMonoMs)
    {
        if (recording) throw new InvalidOperationException("MAP_RECORDING_ALREADY_ACTIVE");
        if (completedResult is not null || finalizationAttempted)
            throw new InvalidOperationException("MAP_RECORDING_ALREADY_COMPLETED");
        recording = true;
        startedAtMonoMs = capturedAtMonoMs;
        lastSampleAtMonoMs = long.MinValue;
        stopReason = null;
        totalObservationBytes = 0;
        currentObservationChunkBytes = 0;
        observationChunkCount = 0;
        localEvidenceSequences.Clear();
        return Status();
    }

    public MapRecordingStatus PushFrame(
        CapturedFrame frame,
        MinimapObservation? minimap = null,
        MapLocalObservation? local = null)
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

        MinimapObservation observation = minimap ?? MinimapGeometryDetector.Observe(frame);
        MapFrameGeometry geometry = observation.Geometry;
        bool independentLocalEvidence = local is not null
            && (local.FrameSequence <= 0 || localEvidenceSequences.Add(local.FrameSequence));
        lastSampleAtMonoMs = frame.CapturedAtMonoMs;
        var recordingObservation = new MapRecordingObservation(
            frame.Sequence,
            frame.CapturedAtMonoMs,
            geometry.Platforms.ToImmutableArray(),
            geometry.Ladders.ToImmutableArray(),
            !independentLocalEvidence || local?.Self is null ? null : observation.Self,
            local?.Geometry.Platforms.ToImmutableArray() ?? [],
            local?.Geometry.Ladders.ToImmutableArray() ?? [],
            independentLocalEvidence ? local?.Self : null,
            local?.FrameSequence);
        int observationBytes = Encoding.UTF8.GetByteCount(
            JsonSerializer.Serialize(recordingObservation, ObservationJsonOptions) + Environment.NewLine);
        if (observationBytes > options.MaxObservationEntryBytes)
        {
            recording = false;
            stopReason = "OBSERVATION_SIZE_LIMIT";
            return Status();
        }
        if (totalObservationBytes + observationBytes > options.MaxTotalObservationBytes)
        {
            recording = false;
            stopReason = "OBSERVATION_SIZE_LIMIT";
            return Status();
        }
        int nextChunkCount = observationChunkCount;
        int nextChunkBytes = currentObservationChunkBytes;
        if (nextChunkBytes == 0 || nextChunkBytes + observationBytes > options.MaxObservationEntryBytes)
        {
            nextChunkCount++;
            nextChunkBytes = observationBytes;
        }
        else
        {
            nextChunkBytes += observationBytes;
        }
        if (nextChunkCount > MaxObservationEntries)
        {
            recording = false;
            stopReason = "OBSERVATION_ENTRY_LIMIT";
            return Status();
        }
        observations.Add(recordingObservation);
        foreach (MapPlatformCandidate candidate in geometry.Platforms) Track(platformTracks, candidate);
        foreach (MapLadderCandidate candidate in geometry.Ladders) Track(ladderTracks, candidate);
        totalObservationBytes += observationBytes;
        observationChunkCount = nextChunkCount;
        currentObservationChunkBytes = nextChunkBytes;
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
        if (finalizationAttempted)
            throw new MapPackageLoadException("MAP_RECORDING_INVALID:FINALIZATION_FAILED", inner: finalizationError);
        recording = false;
        stopReason ??= string.IsNullOrWhiteSpace(reason) ? "OPERATOR_STOPPED" : reason;
        finalizationAttempted = true;
        try
        {
            completedResult = await ExportAsync(stopReason, cancellationToken);
            return completedResult;
        }
        catch (Exception exception)
        {
            finalizationError = exception;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!finalizationAttempted && completedResult is null && (recording || observations.Count > 0))
            await StopAsync("DISPOSED");
    }

    private async Task<MapRecordingResult> ExportAsync(string finalReason, CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
            string baseName = SanitizeFileName(options.MapName);
            string targetPath = Path.Combine(options.OutputDirectory,
                $"{baseName}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.mapzip");
            temporaryPath = targetPath + ".tmp";
            ImmutableArray<MapPlatform> platforms = BuildPlatforms();
            ImmutableArray<MapLadder> ladders = BuildLadders(platforms);
            ImmutableArray<string> qualityReasons = EvaluateQuality(platforms, ladders);
            bool planningReady = qualityReasons.IsEmpty;
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
                recording_sample_count = observations.Count,
                planning_ready = planningReady,
                quality_reasons = qualityReasons
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
                WriteObservationEntries(archive, cancellationToken);
            }
            File.Move(temporaryPath, targetPath, overwrite: false);
            return new MapRecordingResult(
                targetPath, observations.Count, platforms.Length, ladders.Length,
                finalReason, planningReady, qualityReasons);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new MapPackageLoadException("MAP_RECORDING_INVALID:OUTPUT", inner: exception);
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private ImmutableArray<MapPlatform> BuildPlatforms()
    {
        List<PlatformCluster> clusters = platformTracks
            .Where(track => track.Count >= 3 && (track.XMax - track.XMin) / track.Count >= 0.06)
            .Select(track => new PlatformCluster(
                track.XMin / track.Count,
                track.XMax / track.Count,
                track.Y / track.Count,
                track.Count))
            .ToList();
        bool merged;
        do
        {
            merged = false;
            for (int first = 0; first < clusters.Count && !merged; first++)
            for (int second = first + 1; second < clusters.Count; second++)
            {
                PlatformCluster left = clusters[first];
                PlatformCluster right = clusters[second];
                double gap = Math.Max(0, Math.Max(left.XMin, right.XMin) - Math.Min(left.XMax, right.XMax));
                if (Math.Abs(left.Y - right.Y) > 0.025 || gap > 0.020_001) continue;
                clusters[first] = left.Merge(right);
                clusters.RemoveAt(second);
                merged = true;
                break;
            }
        } while (merged);

        return clusters.Select((cluster, index) => new MapPlatform(
                index, cluster.XMin, cluster.XMax, cluster.Y))
            .OrderBy(platform => platform.Y)
            .ThenBy(platform => platform.XMin)
            .Select((platform, index) => platform with { Id = index })
            .ToImmutableArray();
    }

    private ImmutableArray<MapLadder> BuildLadders(ImmutableArray<MapPlatform> platforms)
    {
        unverifiedConnectorCount = 0;
        List<MapLadder> candidates = MergeLadderCandidates(ladderTracks.Where(track => track.Count >= 3)
            .Select(track => CreateLadder(
                track.X / track.Count,
                track.YMin / track.Count,
                track.YMax / track.Count,
                platforms))
            .Where(IsNavigableCandidate));
        List<MapLadder> verified = [];
        HashSet<int> matchedCandidates = [];

        foreach ((double x, double yMin, double yMax) in BuildTrajectoryConnectors())
        {
            int match = Enumerable.Range(0, candidates.Count)
                .Where(index => !matchedCandidates.Contains(index))
                .Where(index => Math.Abs(candidates[index].X - x) <= 0.04
                    && Math.Min(candidates[index].YMax, yMax) + 0.04 >= Math.Max(candidates[index].YMin, yMin))
                .OrderBy(index => Math.Abs(candidates[index].X - x))
                .FirstOrDefault(-1);
            if (match < 0) continue;
            matchedCandidates.Add(match);
            verified.Add(candidates[match]);
        }

        unverifiedConnectorCount = candidates.Count - matchedCandidates.Count;
        return verified.Select((ladder, index) => ladder with { Id = index }).ToImmutableArray();
    }

    private static List<MapLadder> MergeLadderCandidates(IEnumerable<MapLadder> source)
    {
        List<MapLadder> candidates = source.ToList();
        bool merged;
        do
        {
            merged = false;
            for (int first = 0; first < candidates.Count && !merged; first++)
            for (int second = first + 1; second < candidates.Count; second++)
            {
                MapLadder left = candidates[first];
                MapLadder right = candidates[second];
                if (Math.Abs(left.X - right.X) > 0.03
                    || Math.Min(left.YMax, right.YMax) + 0.04 < Math.Max(left.YMin, right.YMin))
                    continue;
                double x = (left.X + right.X) / 2;
                double yMin = Math.Min(left.YMin, right.YMin);
                double yMax = Math.Max(left.YMax, right.YMax);
                ImmutableArray<int> platformIds = left.PlatformIds
                    .Concat(right.PlatformIds)
                    .Distinct()
                    .Order()
                    .ToImmutableArray();
                candidates[first] = new MapLadder(0, x, yMin, yMax, platformIds);
                candidates.RemoveAt(second);
                merged = true;
                break;
            }
        } while (merged);
        return candidates;
    }

    private static bool IsNavigableCandidate(MapLadder ladder) =>
        ladder.X is > 0.03 and < 0.97
        && ladder.YMax - ladder.YMin is >= 0.08 and < 0.65
        && ladder.PlatformIds.Length >= 2;

    private IEnumerable<(double X, double YMin, double YMax)> BuildTrajectoryConnectors()
    {
        List<TrajectorySample> run = [];
        int direction = 0;
        foreach (MapRecordingObservation observation in observations)
        {
            MinimapPoint? point = observation.Self;
            if (point is null)
            {
                foreach ((double x, double yMin, double yMax) in CompleteTrajectoryRun(run))
                    yield return (x, yMin, yMax);
                run = [];
                direction = 0;
                continue;
            }
            if (run.Count == 0)
            {
                run.Add(new TrajectorySample(point, observation.CapturedAtMonoMs, HasNearbyLocalLadder(observation)));
                continue;
            }

            TrajectorySample previousSample = run[^1];
            MinimapPoint previous = previousSample.Point;
            double dx = Math.Abs(point.X - previous.X);
            double dy = point.Y - previous.Y;
            int nextDirection = Math.Abs(dy) < 0.002 ? direction : Math.Sign(dy);
            bool continues = dx <= 0.04
                && Math.Abs(dy) <= 0.25
                && observation.CapturedAtMonoMs - previousSample.CapturedAtMonoMs <= MaximumTrajectoryGapMs
                && (direction == 0 || nextDirection == 0 || nextDirection == direction);
            if (!continues)
            {
                foreach ((double x, double yMin, double yMax) in CompleteTrajectoryRun(run))
                    yield return (x, yMin, yMax);
                run = [new TrajectorySample(point, observation.CapturedAtMonoMs, HasNearbyLocalLadder(observation))];
                direction = 0;
                continue;
            }

            run.Add(new TrajectorySample(point, observation.CapturedAtMonoMs, HasNearbyLocalLadder(observation)));
            if (nextDirection != 0) direction = nextDirection;
        }

        foreach ((double x, double yMin, double yMax) in CompleteTrajectoryRun(run))
            yield return (x, yMin, yMax);
    }

    private static IEnumerable<(double X, double YMin, double YMax)> CompleteTrajectoryRun(List<TrajectorySample> run)
    {
        if (run.Count < 3 || !run.Any(sample => sample.HasLocalLadder)) yield break;
        double yMin = run.Min(sample => sample.Point.Y);
        double yMax = run.Max(sample => sample.Point.Y);
        if (yMax - yMin < 0.08
            || run.Max(sample => sample.Point.X) - run.Min(sample => sample.Point.X) > 0.06)
            yield break;
        yield return (run.Average(sample => sample.Point.X), yMin, yMax);
    }

    private static bool HasNearbyLocalLadder(MapRecordingObservation observation)
    {
        MapLocalSelf? self = observation.LocalSelf;
        if (self is null) return false;
        double centerY = self.Y + self.Height / 2;
        double maximumDistance = Math.Max(0.06, self.Width * 1.5);
        return observation.LocalLadders.Any(ladder =>
            Math.Abs(ladder.X - (self.X + self.Width / 2)) <= maximumDistance
            && centerY >= ladder.YMin - 0.12
            && centerY <= ladder.YMax + 0.12);
    }

    private static MapLadder CreateLadder(
        double x,
        double yMin,
        double yMax,
        ImmutableArray<MapPlatform> platforms)
    {
        ImmutableArray<int> platformIds = platforms
            .Where(platform => x >= platform.XMin - 0.08 && x <= platform.XMax + 0.08
                && platform.Y >= yMin - 0.06 && platform.Y <= yMax + 0.06)
            .Select(platform => platform.Id)
            .ToImmutableArray();
        return new MapLadder(0, x, yMin, yMax, platformIds);
    }

    private ImmutableArray<string> EvaluateQuality(
        ImmutableArray<MapPlatform> platforms,
        ImmutableArray<MapLadder> ladders)
    {
        ImmutableArray<string>.Builder reasons = ImmutableArray.CreateBuilder<string>();
        if (platforms.Length < 2) reasons.Add("PLATFORM_COVERAGE_LOW");
        if (platforms.Length > 64) reasons.Add("PLATFORM_NOISE_HIGH");
        if (ladders.Length > 32) reasons.Add("LADDER_NOISE_HIGH");
        if (unverifiedConnectorCount > 0) reasons.Add("UNVERIFIED_CONNECTORS");
        if (!HasConnectedPlatformGraph(platforms, ladders))
            reasons.Add("CONNECTIVITY_MISSING");
        if (observations.Count(observation => observation.Self is not null) < 3)
            reasons.Add("SELF_TRAJECTORY_LOW");
        if (platforms.Length >= 2 && observations.Count(HasNearbyLocalPlatform) < 3)
            reasons.Add("LOCAL_PLATFORM_EVIDENCE_LOW");
        return reasons.ToImmutable();
    }

    private static bool HasConnectedPlatformGraph(
        ImmutableArray<MapPlatform> platforms,
        ImmutableArray<MapLadder> ladders)
    {
        if (platforms.Length <= 1) return true;
        Dictionary<int, HashSet<int>> neighbors = platforms.ToDictionary(
            platform => platform.Id,
            _ => new HashSet<int>());
        foreach (MapLadder ladder in ladders)
        {
            foreach (int first in ladder.PlatformIds)
            foreach (int second in ladder.PlatformIds)
            {
                if (first != second && neighbors.ContainsKey(first) && neighbors.ContainsKey(second))
                    neighbors[first].Add(second);
            }
        }

        HashSet<int> reached = [platforms[0].Id];
        Queue<int> pending = new([platforms[0].Id]);
        while (pending.TryDequeue(out int current))
        {
            foreach (int neighbor in neighbors[current])
            {
                if (reached.Add(neighbor)) pending.Enqueue(neighbor);
            }
        }
        return reached.Count == platforms.Length;
    }

    private static bool HasNearbyLocalPlatform(MapRecordingObservation observation)
    {
        MapLocalSelf? self = observation.LocalSelf;
        if (self is null) return false;
        double centerX = self.X + self.Width / 2;
        double feetY = self.Y + self.Height;
        return observation.LocalPlatforms.Any(platform =>
            centerX >= platform.XMin - 0.05
            && centerX <= platform.XMax + 0.05
            && Math.Abs(platform.Y - feetY) <= 0.08);
    }

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

    private void WriteObservationEntries(ZipArchive archive, CancellationToken cancellationToken)
    {
        StringBuilder chunk = new();
        int chunkBytes = 0;
        int chunkIndex = 1;
        foreach (MapRecordingObservation observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string line = JsonSerializer.Serialize(observation, ObservationJsonOptions) + Environment.NewLine;
            int lineBytes = Encoding.UTF8.GetByteCount(line);
            if (lineBytes > options.MaxObservationEntryBytes)
                throw new MapPackageLoadException("MAP_RECORDING_INVALID:OBSERVATION_SIZE");
            if (chunkBytes > 0 && chunkBytes + lineBytes > options.MaxObservationEntryBytes)
            {
                WriteEntry(archive, $"recording/observations-{chunkIndex:D4}.jsonl", chunk.ToString());
                chunk.Clear();
                chunkBytes = 0;
                chunkIndex++;
            }
            chunk.Append(line);
            chunkBytes += lineBytes;
        }
        if (chunkBytes > 0)
            WriteEntry(archive, $"recording/observations-{chunkIndex:D4}.jsonl", chunk.ToString());
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string result = string.Concat(name.Select(character => invalid.Contains(character) ? '_' : character));
        if (string.IsNullOrWhiteSpace(result)) return "recorded-map";
        return result.Length <= 80 ? result : result[..80];
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

    private sealed record PlatformCluster(double XMin, double XMax, double Y, int Weight)
    {
        public PlatformCluster Merge(PlatformCluster other)
        {
            int combinedWeight = Weight + other.Weight;
            return new PlatformCluster(
                Math.Min(XMin, other.XMin),
                Math.Max(XMax, other.XMax),
                (Y * Weight + other.Y * other.Weight) / combinedWeight,
                combinedWeight);
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

    private sealed record TrajectorySample(MinimapPoint Point, long CapturedAtMonoMs, bool HasLocalLadder);
}

public sealed record MapRecordingObservation(
    long Sequence,
    long CapturedAtMonoMs,
    ImmutableArray<MapPlatformCandidate> Platforms,
    ImmutableArray<MapLadderCandidate> Ladders,
    MinimapPoint? Self,
    ImmutableArray<MapPlatformCandidate> LocalPlatforms,
    ImmutableArray<MapLadderCandidate> LocalLadders,
    MapLocalSelf? LocalSelf,
    long? LocalFrameSequence);

public sealed record MapLocalSelf(double X, double Y, double Width, double Height);

public sealed record MapLocalObservation(
    MapFrameGeometry Geometry,
    MapLocalSelf? Self,
    long FrameSequence = 0);
