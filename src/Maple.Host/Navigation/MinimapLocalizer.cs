using Maple.Host.Preview;

namespace Maple.Host.Navigation;

public sealed class MinimapLocalizer(MapSignatureMatcher? signatureMatcher = null)
{
    private readonly MapSignatureMatcher matcher = signatureMatcher ?? new MapSignatureMatcher();

    public NavigationLocalization Observe(
        CapturedFrame frame,
        MapPackageSnapshot map,
        NavigationTraversal traversal)
    {
        MapSignatureMatch signature = matcher.Match(frame, map);
        if (map.MinimapRect is not MapMinimapRect roi || signature.FaultCode == "MAP_VIEWPORT_MISMATCH")
            return new NavigationLocalization(frame.Sequence, frame.CapturedAtMonoMs, false, signature.Confidence, null, null, signature.FaultCode);

        MapPoint? self = FindSelf(frame, roi);
        if (self is null)
            return new NavigationLocalization(frame.Sequence, frame.CapturedAtMonoMs, signature.IsMatch, signature.Confidence, null, null, "SELF_NOT_LOCALIZED");

        MapPlatform[] candidates = map.Platforms.Where(platform =>
            self.X >= platform.XMin - 3
            && self.X <= platform.XMax + 3
            && Math.Abs(self.Y - platform.Y) <= 5).ToArray();
        int? platformId = candidates.Length == 1 ? candidates[0].Id : null;
        string? fault = platformId is null && traversal != NavigationTraversal.Connector
            ? "SELF_NOT_LOCALIZED"
            : signature.FaultCode;
        return new NavigationLocalization(
            frame.Sequence,
            frame.CapturedAtMonoMs,
            signature.IsMatch,
            signature.Confidence,
            self,
            platformId,
            fault);
    }

    private static MapPoint? FindSelf(CapturedFrame frame, MapMinimapRect roi)
    {
        bool[] mask = new bool[roi.Width * roi.Height];
        ReadOnlySpan<byte> pixels = frame.BgraPixels.Span;
        for (int y = 0; y < roi.Height; y++)
        for (int x = 0; x < roi.Width; x++)
        {
            int offset = (roi.Y + y) * frame.Stride + (roi.X + x) * 4;
            byte b = pixels[offset];
            byte g = pixels[offset + 1];
            byte r = pixels[offset + 2];
            mask[y * roi.Width + x] = r >= 190 && g >= 170 && b <= 120;
        }

        bool[] visited = new bool[mask.Length];
        Queue<int> pending = new();
        int bestCount = 0;
        double bestX = 0;
        double bestY = 0;
        for (int index = 0; index < mask.Length; index++)
        {
            if (!mask[index] || visited[index]) continue;
            visited[index] = true;
            pending.Enqueue(index);
            int count = 0;
            long sumX = 0;
            long sumY = 0;
            while (pending.TryDequeue(out int current))
            {
                int x = current % roi.Width;
                int y = current / roi.Width;
                count++;
                sumX += x;
                sumY += y;
                for (int nextY = Math.Max(0, y - 1); nextY <= Math.Min(roi.Height - 1, y + 1); nextY++)
                for (int nextX = Math.Max(0, x - 1); nextX <= Math.Min(roi.Width - 1, x + 1); nextX++)
                {
                    int next = nextY * roi.Width + nextX;
                    if (!mask[next] || visited[next]) continue;
                    visited[next] = true;
                    pending.Enqueue(next);
                }
            }
            if (count < 4 || count <= bestCount) continue;
            bestCount = count;
            bestX = sumX / (double)count;
            bestY = sumY / (double)count;
        }
        return bestCount == 0 ? null : new MapPoint(bestX, bestY);
    }
}
