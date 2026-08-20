using Maple.Host.Recognition;

namespace Maple.Host.Tests.Recognition;

public sealed class RecognitionLeaseToggleTests
{
    [Fact]
    public async Task Disabling_releases_the_active_lease_and_reenabling_acquires_a_new_one()
    {
        var leases = new List<FakeLease>();
        await using var toggle = new RecognitionLeaseToggle(_ =>
        {
            var lease = new FakeLease();
            leases.Add(lease);
            return Task.FromResult<IAsyncDisposable>(lease);
        });

        await toggle.SetEnabledAsync(true, CancellationToken.None);
        await toggle.SetEnabledAsync(false, CancellationToken.None);
        await toggle.SetEnabledAsync(true, CancellationToken.None);

        Assert.Equal(2, leases.Count);
        Assert.True(leases[0].Disposed);
        Assert.False(leases[1].Disposed);
    }

    private sealed class FakeLease : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
