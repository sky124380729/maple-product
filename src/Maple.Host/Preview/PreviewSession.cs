namespace Maple.Host.Preview;

public sealed record CapturedFrame(
    int Width,
    int Height,
    int Stride,
    ReadOnlyMemory<byte> BgraPixels,
    long CapturedAtMonoMs,
    long Sequence);

public sealed record PreviewFault(string Code);

public interface IFrameCaptureSource : IAsyncDisposable
{
    event Action<CapturedFrame>? FrameArrived;
    event Action<PreviewFault>? Faulted;
    Task StartAsync(long hwnd, CancellationToken cancellationToken);
    Task StopAsync();
}

public sealed class PreviewSession(IFrameCaptureSource source) : IAsyncDisposable
{
    private long activeHwnd;
    private bool disposed;

    public event Action<CapturedFrame>? FrameArrived;
    public event Action<PreviewFault>? Faulted;

    public async Task StartAsync(long hwnd, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (activeHwnd == hwnd) return;
        try
        {
            if (activeHwnd != 0) await source.StopAsync();
            source.FrameArrived -= OnFrameArrived;
            source.Faulted -= OnFaulted;
            source.FrameArrived += OnFrameArrived;
            source.Faulted += OnFaulted;
            await source.StartAsync(hwnd, cancellationToken);
            activeHwnd = hwnd;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activeHwnd = 0;
            Faulted?.Invoke(new PreviewFault("PREVIEW_START_FAILED:" + exception.GetType().Name));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        source.FrameArrived -= OnFrameArrived;
        source.Faulted -= OnFaulted;
        if (activeHwnd != 0) await source.StopAsync();
        await source.DisposeAsync();
    }

    private void OnFrameArrived(CapturedFrame frame) => FrameArrived?.Invoke(frame);
    private void OnFaulted(PreviewFault fault) => Faulted?.Invoke(fault);
}
