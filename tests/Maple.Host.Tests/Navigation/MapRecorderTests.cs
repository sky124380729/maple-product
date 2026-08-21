using System.IO.Compression;
using System.Text.Json;
using Maple.Host.Navigation;
using Maple.Host.Preview;

namespace Maple.Host.Tests.Navigation;

public sealed class MapRecorderTests
{
    [Fact]
    public async Task Samples_at_five_fps_and_exports_a_loadable_map_package()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions("Recorded Map", directory));
        recorder.Start(1000);
        MinimapObservation minimap = SingleLevelMinimap();
        recorder.PushFrame(Frame(1000, 1), minimap);
        recorder.PushFrame(Frame(1050, 2), minimap);
        recorder.PushFrame(Frame(1200, 3), minimap);
        recorder.PushFrame(Frame(1400, 4), minimap);
        MapRecordingResult result = await recorder.StopAsync("OPERATOR_STOPPED");

        Assert.Equal(3, result.SampleCount);
        Assert.Equal(1, result.PlatformCount);
        Assert.Equal(1, result.LadderCount);
        Assert.True(File.Exists(result.PackagePath));
        await using FileStream stream = File.OpenRead(result.PackagePath);
        MapPackageSnapshot snapshot = await MapPackageLoader.LoadAsync(stream);
        Assert.Equal("Recorded Map", snapshot.Name);
        Assert.Single(snapshot.Platforms);
        Assert.Single(snapshot.Ladders);
        using ZipArchive archive = ZipFile.OpenRead(result.PackagePath);
        Assert.Contains(archive.Entries, entry =>
            entry.FullName.StartsWith("recording/observations-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stops_at_sample_limit_and_keeps_completed_samples()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions("Limited", directory, MaxSamples: 2));
        recorder.Start(1000);
        recorder.PushFrame(Frame(1000, 1));
        recorder.PushFrame(Frame(1200, 2));
        MapRecordingStatus status = recorder.PushFrame(Frame(1400, 3));

        Assert.False(status.IsRecording);
        Assert.Equal("SAMPLE_LIMIT", status.StopReason);
        MapRecordingResult result = await recorder.StopAsync();
        Assert.Equal(2, result.SampleCount);
        Assert.Equal("SAMPLE_LIMIT", result.StopReason);
    }

    [Fact]
    public async Task Requires_three_observations_before_promoting_geometry()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions("Stable", directory));
        recorder.Start(1000);
        MinimapObservation minimap = SingleLevelMinimap();
        recorder.PushFrame(Frame(1000, 1), minimap);
        recorder.PushFrame(Frame(1200, 2), minimap);
        MapRecordingResult beforeStable = await recorder.StopAsync();
        Assert.Equal(0, beforeStable.PlatformCount);

        await using MapRecorder second = new(new MapRecordingOptions("Stable", directory));
        second.Start(1000);
        second.PushFrame(Frame(1000, 1), minimap);
        second.PushFrame(Frame(1200, 2), minimap);
        second.PushFrame(Frame(1400, 3), minimap);
        MapRecordingResult stable = await second.StopAsync();
        Assert.Equal(1, stable.PlatformCount);
    }

    [Fact]
    public async Task Exports_stable_minimap_geometry_and_filters_unlinked_ladders()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions("Global", directory));
        recorder.Start(1000);
        MapFrameGeometry geometry = new(
        [
            new MapPlatformCandidate(0.1, 0.9, 0.3, 0.9),
            new MapPlatformCandidate(0.1, 0.9, 0.7, 0.9)
        ],
        [
            new MapLadderCandidate(0.5, 0.25, 0.75, 0.9),
            new MapLadderCandidate(0.97, 0.05, 0.2, 0.9)
        ]);
        recorder.PushFrame(Frame(1000, 1), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.3, 0.9)), LocalLadderAtSelf());
        recorder.PushFrame(Frame(1200, 2), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.5, 0.9)), LocalLadderAtSelf());
        recorder.PushFrame(Frame(1400, 3), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.7, 0.9)), LocalLadderAtSelf());

        MapRecordingResult result = await recorder.StopAsync();

        Assert.Equal(2, result.PlatformCount);
        Assert.Equal(1, result.LadderCount);
        Assert.True(result.PlanningReady);
        Assert.Empty(result.QualityReasons);
        await using FileStream stream = File.OpenRead(result.PackagePath);
        MapPackageSnapshot snapshot = await MapPackageLoader.LoadAsync(stream);
        Assert.Equal(2, snapshot.Platforms.Length);
        Assert.Single(snapshot.Ladders);
        Assert.Equal(2, snapshot.Ladders[0].PlatformIds.Length);
        Assert.True(snapshot.PlanningReady);
        Assert.Empty(snapshot.QualityReasons);
    }

    [Fact]
    public async Task Infers_a_connector_from_a_stable_vertical_self_trajectory()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions("Trajectory", directory));
        recorder.Start(1000);
        MapFrameGeometry geometry = new(
        [
            new MapPlatformCandidate(0.1, 0.9, 0.3, 0.9),
            new MapPlatformCandidate(0.1, 0.9, 0.7, 0.9)
        ], []);
        recorder.PushFrame(Frame(1000, 1), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.3, 0.9)), LocalLadderAtSelf());
        recorder.PushFrame(Frame(1200, 2), new MinimapObservation(geometry, new MinimapPoint(0.51, 0.5, 0.9)), LocalLadderAtSelf());
        recorder.PushFrame(Frame(1400, 3), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.7, 0.9)), LocalLadderAtSelf());

        MapRecordingResult result = await recorder.StopAsync();

        Assert.Equal(1, result.LadderCount);
        Assert.True(result.PlanningReady);
    }

    [Fact]
    public async Task Does_not_infer_a_climbable_connector_without_local_ladder_evidence()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions("Jump", directory));
        recorder.Start(1000);
        MapFrameGeometry geometry = TwoLevelGeometry();
        MapLocalObservation noLadder = new(new MapFrameGeometry([], []), LocalSelf());
        recorder.PushFrame(Frame(1000, 1), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.3, 0.9)), noLadder);
        recorder.PushFrame(Frame(1200, 2), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.5, 0.9)), noLadder);
        recorder.PushFrame(Frame(1400, 3), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.7, 0.9)), noLadder);

        MapRecordingResult result = await recorder.StopAsync();

        Assert.Equal(0, result.LadderCount);
        Assert.False(result.PlanningReady);
        Assert.Contains("CONNECTIVITY_MISSING", result.QualityReasons);
    }

    [Fact]
    public async Task Does_not_use_a_ladder_far_from_the_recognized_character_as_corroboration()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions("Remote", directory));
        recorder.Start(1000);
        MapFrameGeometry geometry = TwoLevelGeometry();
        MapLocalObservation remote = new(
            new MapFrameGeometry([], [new MapLadderCandidate(0.1, 0.1, 0.8, 0.9)]),
            LocalSelf());
        recorder.PushFrame(Frame(1000, 1), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.3, 0.9)), remote);
        recorder.PushFrame(Frame(1200, 2), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.5, 0.9)), remote);
        recorder.PushFrame(Frame(1400, 3), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.7, 0.9)), remote);

        MapRecordingResult result = await recorder.StopAsync();

        Assert.Equal(0, result.LadderCount);
        Assert.Contains("CONNECTIVITY_MISSING", result.QualityReasons);
    }

    [Fact]
    public async Task Does_not_use_a_minimap_marker_when_recognition_self_is_unavailable()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions("Unverified", directory));
        recorder.Start(1000);
        MapFrameGeometry geometry = TwoLevelGeometry();
        MapLocalObservation local = new(
            new MapFrameGeometry([], [new MapLadderCandidate(0.5, 0.2, 0.8, 0.9)]), null);
        recorder.PushFrame(Frame(1000, 1), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.3, 0.9)), local);
        recorder.PushFrame(Frame(1200, 2), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.5, 0.9)), local);
        recorder.PushFrame(Frame(1400, 3), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.7, 0.9)), local);

        MapRecordingResult result = await recorder.StopAsync();

        Assert.Equal(0, result.LadderCount);
        Assert.Contains("SELF_TRAJECTORY_LOW", result.QualityReasons);
    }

    [Fact]
    public async Task Requires_local_platform_evidence_before_marking_a_map_ready()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions("NoPlatform", directory));
        recorder.Start(1000);
        MapFrameGeometry geometry = TwoLevelGeometry();
        MapLocalObservation ladderOnly = new(
            new MapFrameGeometry([], [new MapLadderCandidate(0.52, 0.2, 0.8, 0.9)]),
            LocalSelf());
        recorder.PushFrame(Frame(1000, 1), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.3, 0.9)), ladderOnly);
        recorder.PushFrame(Frame(1200, 2), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.5, 0.9)), ladderOnly);
        recorder.PushFrame(Frame(1400, 3), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.7, 0.9)), ladderOnly);

        MapRecordingResult result = await recorder.StopAsync();

        Assert.False(result.PlanningReady);
        Assert.Contains("LOCAL_PLATFORM_EVIDENCE_LOW", result.QualityReasons);
    }

    [Fact]
    public async Task Does_not_combine_an_unlinked_self_trajectory_with_an_unrelated_ladder()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions("Unrelated", directory));
        recorder.Start(1000);
        MapFrameGeometry geometry = new(
        [
            new MapPlatformCandidate(0.1, 0.9, 0.3, 0.9),
            new MapPlatformCandidate(0.1, 0.9, 0.7, 0.9)
        ], [new MapLadderCandidate(0.8, 0.25, 0.75, 0.9)]);
        MapLocalObservation local = LocalLadderAtSelf();
        recorder.PushFrame(Frame(1000, 1), new MinimapObservation(geometry, new MinimapPoint(0, 0.3, 0.9)), local);
        recorder.PushFrame(Frame(1200, 2), new MinimapObservation(geometry, new MinimapPoint(0, 0.5, 0.9)), local);
        recorder.PushFrame(Frame(1400, 3), new MinimapObservation(geometry, new MinimapPoint(0, 0.7, 0.9)), local);

        MapRecordingResult result = await recorder.StopAsync();

        Assert.Single(result.QualityReasons, reason => reason == "CONNECTIVITY_MISSING");
        Assert.False(result.PlanningReady);
    }

    [Fact]
    public async Task Does_not_count_one_recognition_frame_as_multiple_local_evidence_samples()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions("Repeated", directory));
        recorder.Start(1000);
        MapFrameGeometry geometry = TwoLevelGeometry();
        MapLocalObservation repeated = LocalLadderAtSelf() with { FrameSequence = 42 };
        recorder.PushFrame(Frame(1000, 1), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.3, 0.9)), repeated);
        recorder.PushFrame(Frame(1200, 2), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.5, 0.9)), repeated);
        recorder.PushFrame(Frame(1400, 3), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.7, 0.9)), repeated);

        MapRecordingResult result = await recorder.StopAsync();

        Assert.False(result.PlanningReady);
        Assert.Contains("SELF_TRAJECTORY_LOW", result.QualityReasons);
        Assert.Contains("LOCAL_PLATFORM_EVIDENCE_LOW", result.QualityReasons);
    }

    [Fact]
    public async Task Does_not_join_vertical_positions_across_a_long_time_gap()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions("Slow", directory));
        recorder.Start(1000);
        MapFrameGeometry geometry = TwoLevelGeometry();
        recorder.PushFrame(Frame(1000, 1), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.3, 0.9)), LocalLadderAtSelf());
        recorder.PushFrame(Frame(1200, 2), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.5, 0.9)), LocalLadderAtSelf());
        recorder.PushFrame(Frame(5000, 3), new MinimapObservation(geometry, new MinimapPoint(0.5, 0.7, 0.9)), LocalLadderAtSelf());

        MapRecordingResult result = await recorder.StopAsync();

        Assert.Equal(0, result.LadderCount);
        Assert.Contains("CONNECTIVITY_MISSING", result.QualityReasons);
    }

    [Fact]
    public async Task Splits_observations_into_loader_safe_archive_entries()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions(
            "Chunked", directory, MaxObservationEntryBytes: 1024));
        recorder.Start(1000);
        MapFrameGeometry geometry = new(
            [new MapPlatformCandidate(0.1, 0.9, 0.5, 0.9)], []);
        for (int index = 0; index < 20; index++)
            recorder.PushFrame(
                Frame(1000 + index * 200, index + 1),
                new MinimapObservation(geometry, new MinimapPoint(0.5, 0.5, 0.9)));

        MapRecordingResult result = await recorder.StopAsync();

        using ZipArchive archive = ZipFile.OpenRead(result.PackagePath);
        ZipArchiveEntry[] entries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("recording/observations-", StringComparison.Ordinal))
            .ToArray();
        Assert.True(entries.Length > 1);
        Assert.All(entries, entry => Assert.InRange(entry.Length, 1, 1024));
        string[] lines = entries.SelectMany(entry =>
        {
            using StreamReader reader = new(entry.Open());
            return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }).ToArray();
        Assert.Equal(20, lines.Length);
        Assert.All(lines, line => JsonDocument.Parse(line).Dispose());
        await using FileStream stream = File.OpenRead(result.PackagePath);
        await MapPackageLoader.LoadAsync(stream);
    }

    [Fact]
    public async Task Does_not_join_vertical_positions_across_missing_self_observations()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions("Gapped", directory));
        recorder.Start(1000);
        MapFrameGeometry geometry = new(
        [
            new MapPlatformCandidate(0.1, 0.9, 0.3, 0.9),
            new MapPlatformCandidate(0.1, 0.9, 0.7, 0.9)
        ], []);
        MinimapPoint?[] points =
        [
            new(0.5, 0.3, 0.9), null,
            new(0.5, 0.5, 0.9), null,
            new(0.5, 0.7, 0.9)
        ];
        for (int index = 0; index < points.Length; index++)
            recorder.PushFrame(
                Frame(1000 + index * 200, index + 1),
                new MinimapObservation(geometry, points[index]));

        MapRecordingResult result = await recorder.StopAsync();

        Assert.Equal(0, result.LadderCount);
        Assert.False(result.PlanningReady);
        Assert.Contains("CONNECTIVITY_MISSING", result.QualityReasons);
    }

    [Fact]
    public async Task Stops_before_the_total_observation_payload_exceeds_the_package_limit()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions(
            "Sized", directory, MaxTotalObservationBytes: 2048));
        recorder.Start(1000);
        MapFrameGeometry geometry = new(
            [new MapPlatformCandidate(0.1, 0.9, 0.5, 0.9)], []);
        MapRecordingStatus status = default!;
        for (int index = 0; index < 20; index++)
        {
            status = recorder.PushFrame(
                Frame(1000 + index * 200, index + 1),
                new MinimapObservation(geometry, new MinimapPoint(0.5, 0.5, 0.9)));
            if (!status.IsRecording) break;
        }

        Assert.False(status.IsRecording);
        Assert.Equal("OBSERVATION_SIZE_LIMIT", status.StopReason);
        Assert.InRange(status.SampleCount, 1, 19);
        MapRecordingResult result = await recorder.StopAsync();
        await using FileStream stream = File.OpenRead(result.PackagePath);
        MapPackageSnapshot snapshot = await MapPackageLoader.LoadAsync(stream);
        Assert.False(snapshot.PlanningReady);
        Assert.Equal(result.QualityReasons, snapshot.QualityReasons);
    }

    [Fact]
    public async Task Stops_before_observation_chunks_exceed_the_loader_entry_limit()
    {
        string directory = CreateTempDirectory();
        await using MapRecorder recorder = new(new MapRecordingOptions(
            "Entries", directory, SampleIntervalMs: 50, MaxObservationEntryBytes: 512));
        recorder.Start(1000);
        MapFrameGeometry geometry = new([new MapPlatformCandidate(0.1, 0.9, 0.5, 0.9)], []);
        MapRecordingStatus status = default!;
        for (int index = 0; index < 6000; index++)
        {
            status = recorder.PushFrame(
                Frame(1000 + index * 50, index + 1),
                new MinimapObservation(geometry, new MinimapPoint(0.5, 0.5, 0.9)));
            if (!status.IsRecording) break;
        }

        Assert.False(status.IsRecording);
        Assert.Equal("OBSERVATION_ENTRY_LIMIT", status.StopReason);
        MapRecordingResult result = await recorder.StopAsync();
        using ZipArchive archive = ZipFile.OpenRead(result.PackagePath);
        Assert.InRange(archive.Entries.Count, 3, 512);
        await using FileStream stream = File.OpenRead(result.PackagePath);
        await MapPackageLoader.LoadAsync(stream);
    }

    [Fact]
    public async Task Failed_export_is_not_retried_during_dispose_and_leaves_no_temporary_package()
    {
        string parent = CreateTempDirectory();
        string outputPath = Path.Combine(parent, "not-a-directory");
        await File.WriteAllTextAsync(outputPath, "occupied");
        MapRecorder recorder = new(new MapRecordingOptions("Failure", outputPath));
        recorder.Start(1000);
        recorder.PushFrame(Frame(1000, 1));

        MapPackageLoadException exception = await Assert.ThrowsAsync<MapPackageLoadException>(
            () => recorder.StopAsync());
        Assert.Equal("MAP_RECORDING_INVALID:OUTPUT", exception.Code);

        await recorder.DisposeAsync();
        Assert.Empty(Directory.GetFiles(parent, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Rejects_a_map_name_that_the_loader_cannot_accept()
    {
        string name = new('x', 257);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MapRecorder(new MapRecordingOptions(name, CreateTempDirectory())));
    }

    private static MapFrameGeometry TwoLevelGeometry() => new(
    [
        new MapPlatformCandidate(0.1, 0.9, 0.3, 0.9),
        new MapPlatformCandidate(0.1, 0.9, 0.7, 0.9)
    ], []);

    private static MinimapObservation SingleLevelMinimap() => new(
        new MapFrameGeometry(
            [new MapPlatformCandidate(0.1, 0.8, 0.6, 0.9)],
            [new MapLadderCandidate(0.5, 0.2, 0.65, 0.9)]),
        null);

    private static MapLocalObservation LocalLadderAtSelf() => new(
        new MapFrameGeometry(
            [new MapPlatformCandidate(0.2, 0.8, 0.6, 0.9)],
            [new MapLadderCandidate(0.5, 0.2, 0.8, 0.9)]),
        LocalSelf());

    private static MapLocalSelf LocalSelf() => new(0.5, 0.5, 0.04, 0.1);

    private static CapturedFrame Frame(long capturedAt, long sequence)
    {
        const int width = 120;
        const int height = 100;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = (y * width + x) * 4;
            if (y == 60 && x is >= 12 and <= 96)
            {
                pixels[offset] = 80;
                pixels[offset + 1] = 180;
                pixels[offset + 2] = 20;
            }
            if (x == 70 && y is >= 20 and <= 65)
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 115;
            pixels[offset + 3] = 255;
        }
        return new CapturedFrame(width, height, width * 4, pixels, capturedAt, sequence);
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "maple-recorder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
