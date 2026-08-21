using Maple.Host.Preview;

namespace Maple.Host.Navigation;

public sealed record MapPlatformCandidate(double XMin, double XMax, double Y, double Confidence);

public sealed record MapLadderCandidate(double X, double YMin, double YMax, double Confidence);

public sealed record MapFrameGeometry(
    IReadOnlyList<MapPlatformCandidate> Platforms,
    IReadOnlyList<MapLadderCandidate> Ladders);

public static class MapFrameGeometryDetector
{
    public static MapFrameGeometry Detect(CapturedFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.BgraPixels.Length < frame.Width * frame.Height * 4)
            return new MapFrameGeometry([], []);

        ReadOnlySpan<byte> pixels = frame.BgraPixels.Span;
        List<Run> platformRuns = FindRuns(frame.Width, frame.Height, pixels, IsGrass, horizontal: true);
        List<Run> ladderRuns = FindRuns(frame.Width, frame.Height, pixels, IsLadder, horizontal: false);
        IReadOnlyList<MapPlatformCandidate> platforms = MergePlatforms(platformRuns, frame.Width, frame.Height);
        IReadOnlyList<MapLadderCandidate> ladders = MergeLadders(ladderRuns, frame.Width, frame.Height)
            .Where(ladder => platforms.Any(platform =>
                ladder.X >= platform.XMin - 0.08 && ladder.X <= platform.XMax + 0.08
                && ladder.YMin <= platform.Y + 0.05
                && ladder.YMax >= platform.Y - 0.05))
            .ToArray();
        return new MapFrameGeometry(
            platforms,
            ladders);
    }

    private static List<Run> FindRuns(int width, int height, ReadOnlySpan<byte> pixels, Func<byte, byte, byte, bool> predicate, bool horizontal)
    {
        int minimumLength = horizontal
            ? Math.Max(8, (int)Math.Round(width * 0.04))
            : Math.Max(20, (int)Math.Round(height * 0.15));
        int firstY = Math.Max(0, (int)Math.Round(height * 0.05));
        int lastY = Math.Min(height - 1, (int)Math.Round(height * 0.90));
        int firstX = Math.Max(0, (int)Math.Round(width * 0.02));
        int lastX = Math.Min(width - 1, (int)Math.Round(width * 0.98));
        List<Run> runs = [];

        if (horizontal)
        {
            for (int y = firstY; y <= lastY; y++)
            {
                int start = -1;
                for (int x = firstX; x <= lastX + 1; x++)
                {
                    bool match = x <= lastX && PixelMatches(pixels, width, x, y, predicate);
                    if (match && start < 0) start = x;
                    if ((!match || x == lastX + 1) && start >= 0)
                    {
                        if (x - start >= minimumLength) runs.Add(new Run(start, x - 1, y, y));
                        start = -1;
                    }
                }
            }
        }
        else
        {
            for (int x = firstX; x <= lastX; x++)
            {
                int start = -1;
                for (int y = firstY; y <= lastY + 1; y++)
                {
                    bool match = y <= lastY && PixelMatches(pixels, width, x, y, predicate);
                    if (match && start < 0) start = y;
                    if ((!match || y == lastY + 1) && start >= 0)
                    {
                        if (y - start >= minimumLength) runs.Add(new Run(x, x, start, y - 1));
                        start = -1;
                    }
                }
            }
        }

        return runs;
    }

    private static IReadOnlyList<MapPlatformCandidate> MergePlatforms(List<Run> runs, int width, int height)
    {
        List<Run> merged = MergeRuns(runs, horizontal: true);
        return merged.Select(run => new MapPlatformCandidate(
            run.Start / (double)width,
            (run.End + 1) / (double)width,
            run.StartY / (double)height,
            Math.Clamp(0.55 + Math.Min(0.4, run.Length / (double)width), 0, 1))).ToArray();
    }

    private static IReadOnlyList<MapLadderCandidate> MergeLadders(List<Run> runs, int width, int height)
    {
        List<Run> merged = MergeRuns(runs, horizontal: false);
        return merged.Select(run => new MapLadderCandidate(
            (run.Start + run.End + 1) / 2d / width,
            run.StartY / (double)height,
            (run.EndY + 1) / (double)height,
            Math.Clamp(0.55 + Math.Min(0.4, run.Length / (double)height), 0, 1))).ToArray();
    }

    private static List<Run> MergeRuns(List<Run> runs, bool horizontal)
    {
        List<Run> result = [];
        foreach (Run run in runs.OrderBy(item => horizontal ? item.StartY : item.Start))
        {
            int index = result.FindIndex(existing =>
                (horizontal
                    ? Math.Abs(existing.StartY - run.StartY) <= 5
                    : Math.Abs(existing.Start - run.Start) <= 3)
                && (Overlap(existing.Start, existing.End, run.Start, run.End) >= Math.Min(existing.Length, run.Length) * 0.5
                    || horizontal && run.Start - existing.End is >= 0 and <= 8));
            if (index < 0)
            {
                result.Add(run);
                continue;
            }

            Run existing = result[index];
            result[index] = horizontal
                ? new Run(Math.Min(existing.Start, run.Start), Math.Max(existing.End, run.End),
                    Math.Min(existing.StartY, run.StartY), Math.Max(existing.EndY, run.EndY))
                : new Run(Math.Min(existing.Start, run.Start), Math.Max(existing.End, run.End),
                    Math.Min(existing.StartY, run.StartY), Math.Max(existing.EndY, run.EndY));
        }
        return result;
    }

    private static int Overlap(int firstStart, int firstEnd, int secondStart, int secondEnd) =>
        Math.Max(0, Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart) + 1);

    private static bool PixelMatches(ReadOnlySpan<byte> pixels, int width, int x, int y, Func<byte, byte, byte, bool> predicate)
    {
        int offset = (y * width + x) * 4;
        return predicate(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }

    private static bool IsGrass(byte b, byte g, byte r) =>
        g >= 100 && g > r * 1.35 && g > b * 1.05;

    private static bool IsLadder(byte b, byte g, byte r) =>
        r is >= 70 and <= 210
        && g is >= 60 and <= 210
        && b is >= 20 and <= 180
        && Math.Abs(g - r) <= 50
        && g >= b * 0.92;

    private readonly record struct Run(int Start, int End, int StartY, int EndY)
    {
        public int Length => Math.Max(End - Start + 1, EndY - StartY + 1);
    }
}
