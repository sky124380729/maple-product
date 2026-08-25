using Maple.Host.Preview;

namespace Maple.Host.Navigation;

public sealed record MapSignatureMatch(
    bool IsMatch,
    double Confidence,
    string? FaultCode,
    int LogicalOffsetX = 0,
    int LogicalOffsetY = 0);

public sealed class MapSignatureMatcher(MapViewportProjection? viewportProjection = null)
{
    private readonly MapViewportProjection projection = viewportProjection ?? new MapViewportProjection();
    private int? calibratedOffsetX;
    private int? calibratedOffsetY;

    public MapSignatureMatch Match(CapturedFrame frame, MapPackageSnapshot map)
    {
        MapMinimapRect? logicalRoi = map.MinimapRect;
        if (logicalRoi is null) return new MapSignatureMatch(false, 0, "MAP_PACKAGE_INVALID");
        if (frame.Stride < frame.Width * 4 || frame.BgraPixels.Length < frame.Stride * frame.Height
            || !projection.TryProject(frame, logicalRoi, map.MinimapReferenceTopInset, out ProjectedMapViewport viewport))
            return new MapSignatureMatch(false, 0, "MAP_VIEWPORT_MISMATCH");
        if (map.Platforms.IsEmpty) return new MapSignatureMatch(false, 0, "MAP_MISMATCH");

        MapMinimapRect roi = viewport.MinimapRect;
        double scale = viewport.Scale;

        int maxShift = Math.Clamp(
            (int)Math.Ceiling(Math.Min(logicalRoi.Width, logicalRoi.Height) * map.Thresholds.Match),
            2,
            12);
        SignatureScore best;
        if (calibratedOffsetX is int knownX && calibratedOffsetY is int knownY)
        {
            best = FindBest(frame, map, logicalRoi, roi, scale,
                Math.Max(-maxShift, knownX - 2), Math.Min(maxShift, knownX + 2),
                Math.Max(-maxShift, knownY - 2), Math.Min(maxShift, knownY + 2));
            if (best.PlatformCoverage < 0.7)
                best = FindBest(frame, map, logicalRoi, roi, scale, -maxShift, maxShift, -maxShift, maxShift);
        }
        else best = FindBest(frame, map, logicalRoi, roi, scale, -maxShift, maxShift, -maxShift, maxShift);

        bool isMatch = best.PlatformCoverage >= 0.7;
        if (isMatch)
        {
            calibratedOffsetX = best.OffsetX;
            calibratedOffsetY = best.OffsetY;
        }
        return new MapSignatureMatch(
            isMatch,
            best.Confidence,
            isMatch ? null : "MAP_MISMATCH",
            best.OffsetX,
            best.OffsetY);
    }

    private static SignatureScore FindBest(
        CapturedFrame frame,
        MapPackageSnapshot map,
        MapMinimapRect logicalRoi,
        MapMinimapRect roi,
        double scale,
        int minimumX,
        int maximumX,
        int minimumY,
        int maximumY)
    {
        SignatureScore best = default;
        bool hasBest = false;
        for (int offsetY = minimumY; offsetY <= maximumY; offsetY++)
        for (int offsetX = minimumX; offsetX <= maximumX; offsetX++)
        {
            SignatureScore candidate = Score(frame, map, logicalRoi, roi, scale, offsetX, offsetY);
            if (hasBest
                && candidate.Confidence < best.Confidence
                || hasBest
                && Math.Abs(candidate.Confidence - best.Confidence) < 0.000_001
                && Math.Abs(offsetX) + Math.Abs(offsetY) >= Math.Abs(best.OffsetX) + Math.Abs(best.OffsetY))
                continue;
            best = candidate;
            hasBest = true;
        }
        return best;
    }

    private static SignatureScore Score(
        CapturedFrame frame,
        MapPackageSnapshot map,
        MapMinimapRect logicalRoi,
        MapMinimapRect roi,
        double scale,
        int offsetX,
        int offsetY)
    {
        int radius = Math.Max(1, (int)Math.Ceiling(scale));
        int expected = 0;
        int matched = 0;
        foreach (MapPlatform platform in map.Platforms)
        {
            int start = Math.Clamp((int)Math.Ceiling(platform.XMin), 0, logicalRoi.Width - 1);
            int end = Math.Clamp((int)Math.Floor(platform.XMax), 0, logicalRoi.Width - 1);
            int y = Math.Clamp((int)Math.Round(platform.Y), 0, logicalRoi.Height - 1);
            for (int x = start; x <= end; x += 2)
            {
                expected++;
                if (HasNearby(frame, roi,
                    MapViewportProjection.ToPhysical(x + offsetX, scale),
                    MapViewportProjection.ToPhysical(y + offsetY, scale),
                    radius,
                    IsPlatformGreen)) matched++;
            }
        }
        double platformCoverage = expected == 0 ? 0 : matched / (double)expected;

        int ladderExpected = 0;
        int ladderMatched = 0;
        foreach (MapLadder ladder in map.Ladders)
        {
            int x = Math.Clamp((int)Math.Round(ladder.X), 0, logicalRoi.Width - 1);
            int start = Math.Clamp((int)Math.Ceiling(ladder.YMin), 0, logicalRoi.Height - 1);
            int end = Math.Clamp((int)Math.Floor(ladder.YMax), 0, logicalRoi.Height - 1);
            for (int y = start; y <= end; y += 2)
            {
                ladderExpected++;
                if (HasNearby(frame, roi,
                    MapViewportProjection.ToPhysical(x + offsetX, scale),
                    MapViewportProjection.ToPhysical(y + offsetY, scale),
                    radius,
                    IsNeutral)) ladderMatched++;
            }
        }
        double ladderCoverage = ladderExpected == 0 ? 1 : ladderMatched / (double)ladderExpected;
        double confidence = Math.Clamp(platformCoverage * 0.8 + ladderCoverage * 0.2, 0, 1);
        return new SignatureScore(platformCoverage, confidence, offsetX, offsetY);
    }

    private static bool HasNearby(
        CapturedFrame frame,
        MapMinimapRect roi,
        int localX,
        int localY,
        int radius,
        Func<byte, byte, byte, bool> predicate)
    {
        ReadOnlySpan<byte> pixels = frame.BgraPixels.Span;
        for (int y = Math.Max(0, localY - radius); y <= Math.Min(roi.Height - 1, localY + radius); y++)
        for (int x = Math.Max(0, localX - radius); x <= Math.Min(roi.Width - 1, localX + radius); x++)
        {
            int offset = (roi.Y + y) * frame.Stride + (roi.X + x) * 4;
            if (predicate(pixels[offset], pixels[offset + 1], pixels[offset + 2])) return true;
        }
        return false;
    }

    private static bool IsPlatformGreen(byte b, byte g, byte r) =>
        g >= 28 && b <= g * 0.75 && g >= r * 0.88;

    private static bool IsNeutral(byte b, byte g, byte r)
    {
        int maximum = Math.Max(b, Math.Max(g, r));
        int minimum = Math.Min(b, Math.Min(g, r));
        return maximum is >= 70 and <= 220 && maximum - minimum <= 28;
    }

    private readonly record struct SignatureScore(
        double PlatformCoverage,
        double Confidence,
        int OffsetX,
        int OffsetY);
}
