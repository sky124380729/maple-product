using System.IO.Compression;
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
        recorder.PushFrame(Frame(1000, 1));
        recorder.PushFrame(Frame(1050, 2));
        recorder.PushFrame(Frame(1200, 3));
        recorder.PushFrame(Frame(1400, 4));
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
        Assert.NotNull(archive.GetEntry("recording/observations.jsonl"));
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
        recorder.PushFrame(Frame(1000, 1));
        recorder.PushFrame(Frame(1200, 2));
        MapRecordingResult beforeStable = await recorder.StopAsync();
        Assert.Equal(0, beforeStable.PlatformCount);

        await using MapRecorder second = new(new MapRecordingOptions("Stable", directory));
        second.Start(1000);
        second.PushFrame(Frame(1000, 1));
        second.PushFrame(Frame(1200, 2));
        second.PushFrame(Frame(1400, 3));
        MapRecordingResult stable = await second.StopAsync();
        Assert.Equal(1, stable.PlatformCount);
    }

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
