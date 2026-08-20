namespace Maple.Host.Recognition;

public sealed class RecognitionLeaseToggle(
    Func<CancellationToken, Task<IAsyncDisposable>> acquire) : IAsyncDisposable
{
    private IAsyncDisposable? lease;

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (enabled)
        {
            lease ??= await acquire(cancellationToken).ConfigureAwait(false);
            return;
        }

        IAsyncDisposable? active = lease;
        lease = null;
        if (active is not null) await active.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        IAsyncDisposable? active = lease;
        lease = null;
        if (active is not null) await active.DisposeAsync().ConfigureAwait(false);
    }
}
