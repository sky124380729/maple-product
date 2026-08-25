namespace Maple.Host.Stationary;

public sealed class StationarySessionRun(
    CancellationTokenSource cancellation,
    Task completion,
    Func<Task> fallbackCleanup,
    TimeSpan? gracefulTimeout = null)
{
    private static readonly TimeSpan DefaultGracefulTimeout = TimeSpan.FromSeconds(2);

    public bool IsCompleted => completion.IsCompleted;

    public async Task<bool> StopAsync()
    {
        if (!completion.IsCompleted)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        try
        {
            await completion.WaitAsync(gracefulTimeout ?? DefaultGracefulTimeout);
            return true;
        }
        catch (TimeoutException)
        {
            await RunBoundedFallbackAsync();
            return false;
        }
        catch
        {
            await RunBoundedFallbackAsync();
            throw;
        }
    }

    private async Task RunBoundedFallbackAsync()
    {
        try
        {
            await fallbackCleanup().WaitAsync(gracefulTimeout ?? DefaultGracefulTimeout);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }
}
