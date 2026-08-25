namespace Maple.Host.Stationary;

public readonly record struct ViewportPoint(double X, double Y);

public static class VisualSetupGeometry
{
    public static FrameRect? MapDragToFrame(
        ViewportPoint start,
        ViewportPoint end,
        double viewportWidth,
        double viewportHeight,
        int frameWidth,
        int frameHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || frameWidth <= 0 || frameHeight <= 0)
            return null;
        double scale = Math.Min(viewportWidth / frameWidth, viewportHeight / frameHeight);
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) return null;
        double displayedWidth = frameWidth * scale;
        double displayedHeight = frameHeight * scale;
        double offsetX = (viewportWidth - displayedWidth) / 2;
        double offsetY = (viewportHeight - displayedHeight) / 2;
        double left = Math.Clamp(Math.Min(start.X, end.X), offsetX, offsetX + displayedWidth);
        double top = Math.Clamp(Math.Min(start.Y, end.Y), offsetY, offsetY + displayedHeight);
        double right = Math.Clamp(Math.Max(start.X, end.X), offsetX, offsetX + displayedWidth);
        double bottom = Math.Clamp(Math.Max(start.Y, end.Y), offsetY, offsetY + displayedHeight);
        int frameLeft = Math.Clamp((int)Math.Floor((left - offsetX) / scale), 0, frameWidth);
        int frameTop = Math.Clamp((int)Math.Floor((top - offsetY) / scale), 0, frameHeight);
        int frameRight = Math.Clamp((int)Math.Ceiling((right - offsetX) / scale), 0, frameWidth);
        int frameBottom = Math.Clamp((int)Math.Ceiling((bottom - offsetY) / scale), 0, frameHeight);
        if (frameRight <= frameLeft || frameBottom <= frameTop) return null;
        return new FrameRect(frameLeft, frameTop, frameRight - frameLeft, frameBottom - frameTop);
    }

    public static (double X, double Y, double Width, double Height) MapFrameRectToViewport(
        FrameRect rectangle,
        double viewportWidth,
        double viewportHeight,
        int frameWidth,
        int frameHeight)
    {
        double scale = Math.Min(viewportWidth / frameWidth, viewportHeight / frameHeight);
        double offsetX = (viewportWidth - frameWidth * scale) / 2;
        double offsetY = (viewportHeight - frameHeight * scale) / 2;
        return (
            offsetX + rectangle.X * scale,
            offsetY + rectangle.Y * scale,
            rectangle.Width * scale,
            rectangle.Height * scale);
    }
}
