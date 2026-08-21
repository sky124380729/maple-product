using Maple.Host.Preview;

namespace Maple.Host.Navigation;

public sealed record MinimapPoint(double X, double Y, double Confidence);

public sealed record MinimapObservation(MapFrameGeometry Geometry, MinimapPoint? Self)
{
    public static MinimapObservation Empty { get; } = new(new MapFrameGeometry([], []), null);
}

public static class MinimapGeometryDetector
{
    private const int MaxContentWidth = 230;
    private const int MaxContentHeight = 180;
    public static MapFrameGeometry Detect(CapturedFrame frame) => Observe(frame).Geometry;

    public static MinimapObservation Observe(CapturedFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.BgraPixels.Length < frame.Stride * frame.Height)
            return MinimapObservation.Empty;

        PixelBounds? content = LocateContent(frame);
        if (content is null) return MinimapObservation.Empty;

        PixelBounds bounds = content.Value;
        bool[] green = BuildMask(frame, bounds, IsPlatformGreen);
        bool[] neutral = BuildMask(frame, bounds, IsNeutralStructure);
        bool[] selfMarker = BuildMask(frame, bounds, IsSelfMarker);
        CloseHorizontalGaps(green, bounds.Width, bounds.Height, 4);
        CloseVerticalGaps(neutral, bounds.Width, bounds.Height, 3);

        IReadOnlyList<MapPlatformCandidate> platforms = Components(green, bounds.Width, bounds.Height)
            .Where(component => component.Width >= Math.Max(10, bounds.Width * 0.06)
                && component.Height <= bounds.Height * 0.12
                && component.Width >= component.Height * 2.5)
            .Select(component => new MapPlatformCandidate(
                component.X / (double)bounds.Width,
                (component.X + component.Width) / (double)bounds.Width,
                component.Y / (double)bounds.Height,
                Math.Clamp(0.55 + component.Width / (double)bounds.Width, 0, 0.95)))
            .ToArray();

        IReadOnlyList<MapLadderCandidate> ladders = Components(neutral, bounds.Width, bounds.Height)
            .Where(component => component.Height >= Math.Max(10, bounds.Height * 0.08)
                && component.Width <= bounds.Width * 0.08
                && component.Height >= component.Width * 2.5)
            .Select(component => new MapLadderCandidate(
                (component.X + component.Width / 2d) / bounds.Width,
                component.Y / (double)bounds.Height,
                (component.Y + component.Height) / (double)bounds.Height,
                Math.Clamp(0.55 + component.Height / (double)bounds.Height, 0, 0.95)))
            .ToArray();

        PixelBounds? marker = Components(selfMarker, bounds.Width, bounds.Height)
            .Where(component => component.Width <= bounds.Width * 0.08
                && component.Height <= bounds.Height * 0.12
                && component.Width * component.Height >= 4)
            .OrderByDescending(component => component.Width * component.Height)
            .FirstOrDefault();
        MinimapPoint? self = marker is null || marker.Value.Width == 0
            ? null
            : new MinimapPoint(
                (marker.Value.X + marker.Value.Width / 2d) / bounds.Width,
                (marker.Value.Y + marker.Value.Height / 2d) / bounds.Height,
                0.85);

        return new MinimapObservation(new MapFrameGeometry(platforms, ladders), self);
    }

    private static PixelBounds? LocateContent(CapturedFrame frame)
    {
        int scanWidth = Math.Min(frame.Width, 280);
        int firstY = Math.Min(frame.Height, 40);
        int lastY = Math.Min(frame.Height - 1, 300);
        if (scanWidth < 80 || lastY - firstY < 30) return null;

        bool[] rowMatches = new bool[lastY - firstY + 1];
        ReadOnlySpan<byte> pixels = frame.BgraPixels.Span;
        for (int y = firstY; y <= lastY; y++)
        {
            int contentPixels = 0;
            for (int x = 0; x < scanWidth; x++)
                if (IsContentPixel(pixels, frame.Stride, x, y)) contentPixels++;
            rowMatches[y - firstY] = contentPixels >= scanWidth * 0.3;
        }

        (int rowStart, int rowLength) = LongestRun(rowMatches);
        if (rowLength < 30) return null;
        int contentY = firstY + rowStart;

        bool[] columnMatches = new bool[scanWidth];
        int probeHeight = Math.Min(30, rowLength);
        for (int x = 0; x < scanWidth; x++)
        {
            int contentPixels = 0;
            for (int y = contentY; y < contentY + probeHeight; y++)
                if (IsContentPixel(pixels, frame.Stride, x, y)) contentPixels++;
            columnMatches[x] = contentPixels >= probeHeight * 0.3;
        }

        (int columnStart, int columnLength) = LongestRun(columnMatches);
        if (columnLength < 80 || columnLength > MaxContentWidth) return null;
        if (columnStart + columnLength >= columnMatches.Length) return null;

        int contentHeight = 0;
        int contentRight = columnStart + columnLength;
        int separatorX = FindRightPanelSeparator(
            pixels, frame.Stride, frame.Width, contentRight, contentY, probeHeight);
        if (separatorX < 0) return null;
        for (int y = contentY; y <= lastY && contentHeight < MaxContentHeight; y++)
        {
            int contentPixels = 0;
            for (int x = columnStart; x < contentRight; x++)
                if (IsContentPixel(pixels, frame.Stride, x, y)) contentPixels++;
            if (contentPixels < columnLength * 0.3
                || !IsBluePanelBorder(pixels, frame.Stride, separatorX, y))
                break;
            contentHeight++;
        }
        if (contentHeight < 30) return null;
        return new PixelBounds(columnStart, contentY, columnLength, contentHeight);
    }

    private static bool[] BuildMask(CapturedFrame frame, PixelBounds bounds, Func<byte, byte, byte, bool> predicate)
    {
        bool[] mask = new bool[bounds.Width * bounds.Height];
        ReadOnlySpan<byte> pixels = frame.BgraPixels.Span;
        for (int y = 0; y < bounds.Height; y++)
        for (int x = 0; x < bounds.Width; x++)
        {
            int offset = (bounds.Y + y) * frame.Stride + (bounds.X + x) * 4;
            mask[y * bounds.Width + x] = predicate(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
        }
        return mask;
    }

    private static IReadOnlyList<PixelBounds> Components(bool[] mask, int width, int height)
    {
        bool[] visited = new bool[mask.Length];
        List<PixelBounds> result = [];
        Queue<int> pending = new();
        for (int index = 0; index < mask.Length; index++)
        {
            if (!mask[index] || visited[index]) continue;
            visited[index] = true;
            pending.Enqueue(index);
            int minX = width;
            int maxX = 0;
            int minY = height;
            int maxY = 0;
            while (pending.TryDequeue(out int current))
            {
                int x = current % width;
                int y = current / width;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                for (int nextY = Math.Max(0, y - 1); nextY <= Math.Min(height - 1, y + 1); nextY++)
                for (int nextX = Math.Max(0, x - 1); nextX <= Math.Min(width - 1, x + 1); nextX++)
                {
                    int next = nextY * width + nextX;
                    if (!mask[next] || visited[next]) continue;
                    visited[next] = true;
                    pending.Enqueue(next);
                }
            }
            result.Add(new PixelBounds(minX, minY, maxX - minX + 1, maxY - minY + 1));
        }
        return result;
    }

    private static void CloseHorizontalGaps(bool[] mask, int width, int height, int maximumGap)
    {
        for (int y = 0; y < height; y++)
        {
            int previous = -1;
            for (int x = 0; x < width; x++)
            {
                if (!mask[y * width + x]) continue;
                if (previous >= 0 && x - previous - 1 <= maximumGap)
                    for (int fill = previous + 1; fill < x; fill++) mask[y * width + fill] = true;
                previous = x;
            }
        }
    }

    private static void CloseVerticalGaps(bool[] mask, int width, int height, int maximumGap)
    {
        for (int x = 0; x < width; x++)
        {
            int previous = -1;
            for (int y = 0; y < height; y++)
            {
                if (!mask[y * width + x]) continue;
                if (previous >= 0 && y - previous - 1 <= maximumGap)
                    for (int fill = previous + 1; fill < y; fill++) mask[fill * width + x] = true;
                previous = y;
            }
        }
    }

    private static (int Start, int Length) LongestRun(bool[] values)
    {
        int bestStart = 0;
        int bestLength = 0;
        int currentStart = 0;
        int currentLength = 0;
        for (int index = 0; index <= values.Length; index++)
        {
            if (index < values.Length && values[index])
            {
                if (currentLength == 0) currentStart = index;
                currentLength++;
                continue;
            }
            if (currentLength > bestLength) (bestStart, bestLength) = (currentStart, currentLength);
            currentLength = 0;
        }
        return (bestStart, bestLength);
    }

    private static bool IsDark(ReadOnlySpan<byte> pixels, int stride, int x, int y)
    {
        int offset = y * stride + x * 4;
        return Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2])) <= 90;
    }

    private static bool IsContentPixel(ReadOnlySpan<byte> pixels, int stride, int x, int y)
    {
        int offset = y * stride + x * 4;
        byte b = pixels[offset];
        byte g = pixels[offset + 1];
        byte r = pixels[offset + 2];
        return Math.Max(b, Math.Max(g, r)) <= 90
            || IsPlatformGreen(b, g, r)
            || IsSelfMarker(b, g, r);
    }

    private static int FindRightPanelSeparator(
        ReadOnlySpan<byte> pixels,
        int stride,
        int frameWidth,
        int contentRight,
        int contentY,
        int probeHeight)
    {
        for (int x = contentRight; x < Math.Min(frameWidth, contentRight + 8); x++)
        {
            int matches = 0;
            for (int y = contentY; y < contentY + probeHeight; y++)
                if (IsBluePanelBorder(pixels, stride, x, y)) matches++;
            if (matches >= probeHeight * 0.7) return x;
        }
        return -1;
    }

    private static bool IsBluePanelBorder(ReadOnlySpan<byte> pixels, int stride, int x, int y)
    {
        int offset = y * stride + x * 4;
        byte b = pixels[offset];
        byte g = pixels[offset + 1];
        byte r = pixels[offset + 2];
        return b >= 100 && b > r * 1.15 && b > g * 1.05;
    }

    private static bool IsPlatformGreen(byte b, byte g, byte r) =>
        g >= 70 && g > r * 1.25 && g > b * 1.05;

    private static bool IsNeutralStructure(byte b, byte g, byte r)
    {
        int maximum = Math.Max(b, Math.Max(g, r));
        int minimum = Math.Min(b, Math.Min(g, r));
        return maximum is >= 70 and <= 220 && maximum - minimum <= 28;
    }

    private static bool IsSelfMarker(byte b, byte g, byte r) =>
        r >= 190 && g >= 170 && b <= 120;

    private readonly record struct PixelBounds(int X, int Y, int Width, int Height);
}
