using Maple.Host.Preview;
using Maple.Host.Windows;

namespace Maple.Host.Recognition;

public enum RecognitionLeaseKind { Preview, Stationary, Navigation }

public sealed class RecognitionSession : IAsyncDisposable
{
    private readonly IFrameCaptureSource? source;
    private readonly IRecognitionProvider provider;
    private readonly object gate = new();
    private CancellationTokenSource runCts = new();
    private Task? worker;
    private CapturedFrame? latestFrame;
    private SemaphoreSlim frameSignal = new(0, 1);
    private int leases;
    private bool sourceStarted;
    private bool disposed;
    private WindowIdentity? target;
    private readonly string sessionId = Guid.NewGuid().ToString("N");

    public RecognitionSession(IFrameCaptureSource source, IRecognitionProvider provider)
    {
        this.source = source;
        this.provider = provider;
        source.FrameArrived += OnFrameArrived;
    }

    public RecognitionSession(IRecognitionProvider provider)
    {
        this.provider = provider;
    }

    public event Action<RecognitionSnapshot>? SnapshotPublished;
    public RecognitionSnapshot? Latest { get; private set; }

    public async Task<IAsyncDisposable> AcquireAsync(
        RecognitionLeaseKind kind,
        WindowIdentity requestedTarget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestedTarget);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (target is not null && target != requestedTarget)
                throw new InvalidOperationException("RECOGNITION_TARGET_CHANGED");
            target = requestedTarget;
            if (leases == 0 && runCts.IsCancellationRequested)
                runCts = new CancellationTokenSource();
            leases++;
        }

        try
        {
            if (!sourceStarted)
            {
                if (source is not null)
                    await source.StartAsync(requestedTarget.Hwnd, cancellationToken).ConfigureAwait(false);
                lock (gate)
                {
                    sourceStarted = true;
                    worker ??= Task.Run(ProcessFramesAsync);
                }
            }
            return new Lease(this);
        }
        catch
        {
            await ReleaseAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? activeWorker;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            leases = 0;
            runCts.Cancel();
            activeWorker = worker;
        }
        if (activeWorker is not null) await activeWorker.ConfigureAwait(false);
        if (sourceStarted && source is not null) await source.StopAsync().ConfigureAwait(false);
        if (source is not null)
        {
            source.FrameArrived -= OnFrameArrived;
            await source.DisposeAsync().ConfigureAwait(false);
        }
        if (provider is IAsyncDisposable asyncProvider)
            await asyncProvider.DisposeAsync().ConfigureAwait(false);
        frameSignal.Dispose();
        runCts.Dispose();
    }

    private async Task ReleaseAsync()
    {
        bool stop;
        lock (gate)
        {
            if (leases == 0) return;
            leases--;
            stop = leases == 0;
            if (stop) runCts.Cancel();
        }
        if (stop && worker is not null) await worker.ConfigureAwait(false);
        if (stop && sourceStarted)
        {
            if (source is not null) await source.StopAsync().ConfigureAwait(false);
            sourceStarted = false;
            worker = null;
            lock (gate) target = null;
        }
    }

    private void OnFrameArrived(CapturedFrame frame)
    {
        lock (gate)
        {
            if (disposed || leases == 0) return;
            latestFrame = frame;
        }
        if (frameSignal.CurrentCount == 0) frameSignal.Release();
    }

    public void PushFrame(CapturedFrame frame) => OnFrameArrived(frame);

    private async Task ProcessFramesAsync()
    {
        try
        {
            while (!runCts.IsCancellationRequested)
            {
                await frameSignal.WaitAsync(runCts.Token).ConfigureAwait(false);
                CapturedFrame? frame;
                lock (gate) frame = latestFrame;
                if (frame is null) continue;
                try
                {
                    RecognitionAnalysis analysis = await provider.AnalyzeAsync(frame, runCts.Token).ConfigureAwait(false);
                    WindowIdentity? currentTarget;
                    lock (gate) currentTarget = target;
                    var snapshot = RecognitionSnapshot.Create(
                        sessionId, currentTarget, frame.Sequence, frame.CapturedAtMonoMs,
                        Environment.TickCount64, analysis.Hud, analysis.Monsters,
                        analysis.Drops, analysis.OtherPlayers, analysis.Self,
                        geometry: analysis.Geometry) with
                    {
                        FrameWidth = frame.Width,
                        FrameHeight = frame.Height
                    };
                    Latest = snapshot;
                    SnapshotPublished?.Invoke(snapshot);
                }
                catch (OperationCanceledException) when (runCts.IsCancellationRequested) { }
                catch (Exception)
                {
                    var fault = (Latest ?? RecognitionSnapshot.Create(
                        sessionId, target, frame.Sequence, frame.CapturedAtMonoMs,
                        Environment.TickCount64, HudObservation.Empty, [], [], [], null))
                        .WithHealth(RecognitionHealth.Faulted, "RECOGNITION_PROVIDER_FAILED");
                    Latest = fault;
                    SnapshotPublished?.Invoke(fault);
                }
            }
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested) { }
    }

    private sealed class Lease(RecognitionSession owner) : IAsyncDisposable
    {
        private int released;
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref released, 1) == 0
            ? new ValueTask(owner.ReleaseAsync())
            : ValueTask.CompletedTask;
    }
}
