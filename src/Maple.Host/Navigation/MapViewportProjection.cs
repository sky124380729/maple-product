using Maple.Host.Preview;

namespace Maple.Host.Navigation;

public sealed record ProjectedMapViewport(MapMinimapRect MinimapRect, double Scale);

public sealed class MapViewportProjection(int referenceWidth = 1366, int referenceHeight = 768)
{
    public bool TryProject(
        CapturedFrame frame,
        MapMinimapRect logicalRect,
        out ProjectedMapViewport projected) =>
        TryProject(frame, logicalRect, 0, out projected);

    public bool TryProject(
        CapturedFrame frame,
        MapMinimapRect logicalRect,
        int referenceTopInset,
        out ProjectedMapViewport projected)
    {
        projected = default!;
        if (referenceWidth <= 0 || referenceHeight <= 0 || frame.Width <= 0 || frame.Height <= 0)
            return false;
        double scaleX = frame.Width / (double)referenceWidth;
        double scaleY = frame.Height / (double)referenceHeight;
        if (!double.IsFinite(scaleX) || !double.IsFinite(scaleY) || scaleX <= 0 || scaleY <= 0)
            return false;
        if (Math.Abs(scaleX - scaleY) / Math.Max(scaleX, scaleY) > 0.05)
            return false;

        int x = (int)Math.Round(logicalRect.X * scaleX);
        int y = (int)Math.Round((logicalRect.Y - referenceTopInset) * scaleX);
        int width = Math.Max(1, (int)Math.Round(logicalRect.Width * scaleX));
        int height = Math.Max(1, (int)Math.Round(logicalRect.Height * scaleX));
        if (x < 0 || y < 0 || x + width > frame.Width || y + height > frame.Height)
            return false;
        projected = new ProjectedMapViewport(new MapMinimapRect(x, y, width, height), scaleX);
        return true;
    }

    public static int ToPhysical(double logicalCoordinate, double scale) =>
        (int)Math.Round(logicalCoordinate * scale);

    public static double ToLogical(double physicalCoordinate, double scale) =>
        physicalCoordinate / scale;
}
