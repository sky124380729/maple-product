using Maple.Host.Preview;

namespace Maple.Host.Tests.Preview;

public sealed class PreviewSessionTests
{
    [Fact]
    public async Task Reuses_capture_until_target_changes()
    {
        var source = new RecordingFrameCaptureSource();
        await using var session = new PreviewSession(source);

        await session.StartAsync(100, CancellationToken.None);
        await session.StartAsync(100, CancellationToken.None);
        await session.StartAsync(200, CancellationToken.None);

        Assert.Equal([100, 200], source.StartedTargets);
        Assert.Equal(1, source.StopCount);
    }

    [Fact]
    public async Task Capture_fault_is_reported_without_throwing_to_session_owner()
    {
        var source = new RecordingFrameCaptureSource { StartException = new InvalidOperationException("capture") };
        await using var session = new PreviewSession(source);
        PreviewFault? fault = null;
        session.Faulted += value => fault = value;

        await session.StartAsync(100, CancellationToken.None);

        Assert.Equal("PREVIEW_START_FAILED:InvalidOperationException", fault?.Code);
    }

    private sealed class RecordingFrameCaptureSource : IFrameCaptureSource
    {
        public List<long> StartedTargets { get; } = [];
        public int StopCount { get; private set; }
        public Exception? StartException { get; init; }
        public event Action<CapturedFrame>? FrameArrived { add { } remove { } }
        public event Action<PreviewFault>? Faulted { add { } remove { } }

        public Task StartAsync(long hwnd, CancellationToken cancellationToken)
        {
            if (StartException is not null) throw StartException;
            StartedTargets.Add(hwnd);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
