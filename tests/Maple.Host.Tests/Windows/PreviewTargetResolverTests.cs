using Maple.Host.Windows;

namespace Maple.Host.Tests.Windows;

public sealed class PreviewTargetResolverTests
{
    [Fact]
    public async Task Discovers_the_unique_client_when_no_session_is_bound()
    {
        WindowIdentity target = Target(101);
        var locator = new RecordingWindowLocator([target]);

        PreviewTargetResolution result = await PreviewTargetResolver.ResolveAsync(
            locator,
            boundTarget: null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(target, result.Target);
        Assert.Equal(1, locator.CallCount);
    }

    [Fact]
    public async Task Reuses_bound_target_without_enumerating_windows()
    {
        WindowIdentity bound = Target(202);
        var locator = new RecordingWindowLocator([Target(303)]);

        PreviewTargetResolution result = await PreviewTargetResolver.ResolveAsync(
            locator,
            bound,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(bound, result.Target);
        Assert.Equal(0, locator.CallCount);
    }

    [Fact]
    public async Task Rejects_zero_or_multiple_idle_candidates_without_capture()
    {
        foreach (IReadOnlyList<WindowIdentity> candidates in new[]
        {
            Array.Empty<WindowIdentity>(),
            new[] { Target(1), Target(2) }
        })
        {
            var locator = new RecordingWindowLocator(candidates);

            PreviewTargetResolution result = await PreviewTargetResolver.ResolveAsync(
                locator,
                boundTarget: null,
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(candidates.Count == 0 ? "TARGET_NOT_FOUND" : "TARGET_MULTIPLE", result.Code);
            Assert.Null(result.Target);
        }
    }

    private static WindowIdentity Target(long hwnd) =>
        new(hwnd, (int)hwnd, $@"C:\Maple\Maplestory_{hwnd}.exe", hwnd);

    private sealed class RecordingWindowLocator(IReadOnlyList<WindowIdentity> candidates) : IWindowLocator
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<WindowIdentity>> FindRunningMapleClientsAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(candidates);
        }
    }
}
