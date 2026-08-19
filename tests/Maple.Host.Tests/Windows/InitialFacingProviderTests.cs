using Maple.Core.Movement;
using Maple.Host.Windows;

namespace Maple.Host.Tests.Windows;

public sealed class InitialFacingProviderTests
{
    [Theory]
    [InlineData("left", MovementDirection.Left)]
    [InlineData("LEFT", MovementDirection.Left)]
    [InlineData("right", MovementDirection.Right)]
    public async Task Resolves_manual_selection(
        string selection,
        MovementDirection expected)
    {
        var provider = new ManualInitialFacingProvider();

        InitialFacingResolution result = await provider.ResolveAsync(
            Target(),
            selection,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(expected, result.Direction);
        Assert.Equal("manual", result.Source);
        Assert.Null(result.Confidence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("up")]
    public async Task Rejects_missing_or_unknown_selection(string? selection)
    {
        var provider = new ManualInitialFacingProvider();

        InitialFacingResolution result = await provider.ResolveAsync(
            Target(),
            selection,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INITIAL_FACING_INVALID", result.Code);
        Assert.Null(result.Direction);
    }

    private static WindowIdentity Target() =>
        new(100, 42, @"C:\Games\MapleStory.exe", 123_456);
}
