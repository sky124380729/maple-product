using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class StationarySessionRunTests
{
    [Fact]
    public async Task Completion_state_tracks_the_underlying_session_task()
    {
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = new StationarySessionRun(
            cancellation,
            completion.Task,
            () => Task.CompletedTask);

        Assert.False(run.IsCompleted);

        completion.TrySetResult();
        await completion.Task;

        Assert.True(run.IsCompleted);
    }

    [Fact]
    public async Task Stop_waits_for_controller_release_before_fallback_cleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var keyUpCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        Task completion = Task.Run(async () =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }

            events.Add("KeyUpAndOffsetCommitted");
            keyUpCompleted.TrySetResult();
            await allowCleanup.Task;
            events.Add("ConnectionDisposed");
        });
        bool fallbackCalled = false;
        var run = new StationarySessionRun(
            cancellation,
            completion,
            () =>
            {
                fallbackCalled = true;
                return Task.CompletedTask;
            },
            TimeSpan.FromSeconds(1));
        await started.Task;

        Task<bool> stopping = run.StopAsync();
        await keyUpCompleted.Task;

        Assert.False(stopping.IsCompleted);
        Assert.False(fallbackCalled);
        allowCleanup.TrySetResult();
        Assert.True(await stopping);
        Assert.Equal(["KeyUpAndOffsetCommitted", "ConnectionDisposed"], events);
    }

    [Fact]
    public async Task Stop_uses_fallback_cleanup_after_the_grace_period()
    {
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fallbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fallbackCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = new StationarySessionRun(
            cancellation,
            completion.Task,
            () =>
            {
                fallbackStarted.TrySetResult();
                return fallbackCompletion.Task;
            },
            TimeSpan.FromMilliseconds(10));

        bool graceful;
        bool fallbackWasIncomplete;
        try
        {
            graceful = await run.StopAsync().WaitAsync(TimeSpan.FromMilliseconds(100));
            fallbackWasIncomplete = !fallbackCompletion.Task.IsCompleted;
        }
        finally
        {
            completion.TrySetResult();
            fallbackCompletion.TrySetResult();
        }

        Assert.False(graceful);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(fallbackStarted.Task.IsCompletedSuccessfully);
        Assert.True(fallbackWasIncomplete);
    }

    [Fact]
    public async Task Stop_waits_for_completion_when_cancellation_was_already_disposed()
    {
        var cancellation = new CancellationTokenSource();
        cancellation.Dispose();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = new StationarySessionRun(
            cancellation,
            completion.Task,
            () => Task.CompletedTask,
            TimeSpan.FromSeconds(1));

        Task<bool> stopping = run.StopAsync();
        await Task.Yield();

        Assert.False(stopping.IsCompleted);
        completion.TrySetResult();
        Assert.True(await stopping);
    }
}
