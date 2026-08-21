using Maple.Host.Preview;
using Maple.Host.Navigation;
using Maple.Host.Recognition;
using Maple.Host.Windows;

namespace Maple.Host.Tests.Recognition;

public sealed class RecognitionSessionTests
{
    [Fact]
    public async Task Multiple_leases_share_one_capture_and_last_release_stops_it()
    {
        var source = new FakeCaptureSource();
        await using var session = new RecognitionSession(source, new FakeProvider());
        var target = new WindowIdentity(7, 8, "maple.exe", 9);

        await using var preview = await session.AcquireAsync(RecognitionLeaseKind.Preview, target, CancellationToken.None);
        await using var stationary = await session.AcquireAsync(RecognitionLeaseKind.Stationary, target, CancellationToken.None);

        Assert.Equal(1, source.StartCount);
        await preview.DisposeAsync();
        Assert.Equal(0, source.StopCount);
        await stationary.DisposeAsync();
        Assert.Equal(1, source.StopCount);
    }

    [Fact]
    public async Task Provider_failure_is_published_as_fault()
    {
        var source = new FakeCaptureSource();
        var provider = new FakeProvider { Exception = new InvalidOperationException("model") };
        await using var session = new RecognitionSession(source, provider);
        var observed = new TaskCompletionSource<RecognitionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.SnapshotPublished += snapshot => observed.TrySetResult(snapshot);
        await using var lease = await session.AcquireAsync(RecognitionLeaseKind.Preview, new WindowIdentity(1, 2, "x", 3), CancellationToken.None);

        source.Emit(new CapturedFrame(1, 1, 4, new byte[4], 10, 1));
        RecognitionSnapshot snapshot = await observed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(RecognitionHealth.Faulted, snapshot.Health);
        Assert.Equal("RECOGNITION_PROVIDER_FAILED", snapshot.FaultCode);
    }

    [Fact]
    public async Task Publishes_provider_map_geometry_with_the_snapshot()
    {
        var source = new FakeCaptureSource();
        var geometry = new MapFrameGeometry(
            [new MapPlatformCandidate(0.1, 0.9, 0.5, 0.8)],
            [new MapLadderCandidate(0.5, 0.2, 0.5, 0.7)]);
        var provider = new FakeProvider
        {
            Analysis = RecognitionAnalysis.Empty with { Geometry = geometry }
        };
        await using var session = new RecognitionSession(source, provider);
        var observed = new TaskCompletionSource<RecognitionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.SnapshotPublished += snapshot => observed.TrySetResult(snapshot);
        await using var lease = await session.AcquireAsync(RecognitionLeaseKind.Preview, new WindowIdentity(1, 2, "x", 3), CancellationToken.None);

        source.Emit(new CapturedFrame(320, 240, 1280, new byte[320 * 240 * 4], 10, 1));
        RecognitionSnapshot snapshot = await observed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Same(geometry, snapshot.Geometry);
        Assert.Equal(320, snapshot.FrameWidth);
        Assert.Equal(240, snapshot.FrameHeight);
    }

    private sealed class FakeProvider : IRecognitionProvider
    {
        public Exception? Exception { get; init; }
        public RecognitionAnalysis Analysis { get; init; } = RecognitionAnalysis.Empty;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<RecognitionAnalysis> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (Exception is not null) throw Exception;
            return Task.FromResult(Analysis);
        }
    }

    private sealed class FakeCaptureSource : IFrameCaptureSource
    {
#pragma warning disable CS0067
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public event Action<CapturedFrame>? FrameArrived;
        public event Action<PreviewFault>? Faulted;
        public Task StartAsync(long hwnd, CancellationToken cancellationToken) { StartCount++; return Task.CompletedTask; }
        public Task StopAsync() { StopCount++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Emit(CapturedFrame frame) => FrameArrived?.Invoke(frame);
#pragma warning restore CS0067
    }
}
