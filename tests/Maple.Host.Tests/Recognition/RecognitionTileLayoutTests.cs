using Maple.Host.Recognition;

namespace Maple.Host.Tests.Recognition;

public sealed class RecognitionTileLayoutTests
{
    [Fact]
    public void Covers_full_frame_and_keeps_edge_tiles_inside_bounds()
    {
        IReadOnlyList<RecognitionTile> tiles = RecognitionTileLayout.Build(2027, 1142);

        Assert.Contains(tiles, tile => tile.X == 0 && tile.Y == 0);
        Assert.Contains(tiles, tile => tile.X + tile.Width == 2027 && tile.Y + tile.Height == 1142);
        Assert.All(tiles, tile =>
        {
            Assert.InRange(tile.X, 0, 2026);
            Assert.InRange(tile.Y, 0, 1141);
            Assert.InRange(tile.Width, 1, 640);
            Assert.InRange(tile.Height, 1, 640);
            Assert.True(tile.X + tile.Width <= 2027);
            Assert.True(tile.Y + tile.Height <= 1142);
        });
    }
}
