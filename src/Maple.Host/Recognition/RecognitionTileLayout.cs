namespace Maple.Host.Recognition;

public readonly record struct RecognitionTile(int X, int Y, int Width, int Height);

public static class RecognitionTileLayout
{
    public static IReadOnlyList<RecognitionTile> Build(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        const int tileSize = 640;
        const int step = 512;
        if (width <= tileSize && height <= tileSize)
            return [new RecognitionTile(0, 0, width, height)];
        var tiles = new List<RecognitionTile>();
        foreach (int y in Starts(height, tileSize, step))
            foreach (int x in Starts(width, tileSize, step))
                tiles.Add(new RecognitionTile(x, y, Math.Min(tileSize, width - x), Math.Min(tileSize, height - y)));
        return tiles;
    }

    private static IReadOnlyList<int> Starts(int extent, int size, int step)
    {
        var starts = new List<int>();
        for (int start = 0; start + size < extent; start += step) starts.Add(start);
        int last = Math.Max(0, extent - size);
        if (starts.Count == 0 || starts[^1] != last) starts.Add(last);
        return starts;
    }
}
