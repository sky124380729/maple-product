using Maple.Host.Preview;

namespace Maple.Host.Navigation;

public sealed record MapSignatureMatch(bool IsMatch, double Confidence, string? FaultCode);

public sealed class MapSignatureMatcher
{
    public MapSignatureMatch Match(CapturedFrame frame, MapPackageSnapshot map)
    {
        MapMinimapRect? roi = map.MinimapRect;
        if (roi is null) return new MapSignatureMatch(false, 0, "MAP_PACKAGE_INVALID");
        if (roi.X < 0 || roi.Y < 0 || roi.X + roi.Width > frame.Width || roi.Y + roi.Height > frame.Height
            || frame.Stride < frame.Width * 4 || frame.BgraPixels.Length < frame.Stride * frame.Height)
            return new MapSignatureMatch(false, 0, "MAP_VIEWPORT_MISMATCH");
        if (map.Platforms.IsEmpty) return new MapSignatureMatch(false, 0, "MAP_MISMATCH");

        int expected = 0;
        int matched = 0;
        foreach (MapPlatform platform in map.Platforms)
        {
            int start = Math.Clamp((int)Math.Ceiling(platform.XMin), 0, roi.Width - 1);
            int end = Math.Clamp((int)Math.Floor(platform.XMax), 0, roi.Width - 1);
            int y = Math.Clamp((int)Math.Round(platform.Y), 0, roi.Height - 1);
            for (int x = start; x <= end; x += 2)
            {
                expected++;
                if (HasNearby(frame, roi, x, y, IsPlatformGreen)) matched++;
            }
        }
        double platformCoverage = expected == 0 ? 0 : matched / (double)expected;

        int ladderExpected = 0;
        int ladderMatched = 0;
        foreach (MapLadder ladder in map.Ladders)
        {
            int x = Math.Clamp((int)Math.Round(ladder.X), 0, roi.Width - 1);
            int start = Math.Clamp((int)Math.Ceiling(ladder.YMin), 0, roi.Height - 1);
            int end = Math.Clamp((int)Math.Floor(ladder.YMax), 0, roi.Height - 1);
            for (int y = start; y <= end; y += 2)
            {
                ladderExpected++;
                if (HasNearby(frame, roi, x, y, IsNeutral)) ladderMatched++;
            }
        }
        double ladderCoverage = ladderExpected == 0 ? 1 : ladderMatched / (double)ladderExpected;
        double confidence = Math.Clamp(platformCoverage * 0.8 + ladderCoverage * 0.2, 0, 1);
        bool isMatch = platformCoverage >= 0.7;
        return new MapSignatureMatch(isMatch, confidence, isMatch ? null : "MAP_MISMATCH");
    }

    private static bool HasNearby(
        CapturedFrame frame,
        MapMinimapRect roi,
        int localX,
        int localY,
        Func<byte, byte, byte, bool> predicate)
    {
        ReadOnlySpan<byte> pixels = frame.BgraPixels.Span;
        for (int y = Math.Max(0, localY - 1); y <= Math.Min(roi.Height - 1, localY + 1); y++)
        for (int x = Math.Max(0, localX - 1); x <= Math.Min(roi.Width - 1, localX + 1); x++)
        {
            int offset = (roi.Y + y) * frame.Stride + (roi.X + x) * 4;
            if (predicate(pixels[offset], pixels[offset + 1], pixels[offset + 2])) return true;
        }
        return false;
    }

    private static bool IsPlatformGreen(byte b, byte g, byte r) =>
        g >= 70 && g > r * 1.25 && g > b * 1.05;

    private static bool IsNeutral(byte b, byte g, byte r)
    {
        int maximum = Math.Max(b, Math.Max(g, r));
        int minimum = Math.Min(b, Math.Min(g, r));
        return maximum is >= 70 and <= 220 && maximum - minimum <= 28;
    }
}
