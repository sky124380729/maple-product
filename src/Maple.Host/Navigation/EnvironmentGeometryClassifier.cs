using Maple.Host.Recognition;

namespace Maple.Host.Navigation;

public static class EnvironmentGeometryClassifier
{
    public static MapFrameGeometry Classify(
        IEnumerable<RecognitionTarget> detections,
        int frameWidth,
        int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
            return new MapFrameGeometry([], []);

        List<MapPlatformCandidate> platforms = [];
        List<MapLadderCandidate> ladders = [];
        foreach (RecognitionTarget detection in detections)
        {
            if (!string.Equals(detection.Kind, "environment", StringComparison.OrdinalIgnoreCase)
                || detection.Confidence < 0.4
                || detection.Width <= 0
                || detection.Height <= 0)
                continue;

            double x = detection.X / frameWidth;
            double y = detection.Y / frameHeight;
            double width = detection.Width / frameWidth;
            double height = detection.Height / frameHeight;
            if (y >= 0.9) continue;

            bool platform = width >= 0.08 && height <= 0.04 && width / height >= 4;
            if (platform && !(y < 0.12 && (x < 0.14 || x + width > 0.82)))
            {
                platforms.Add(new MapPlatformCandidate(
                    Math.Clamp(x, 0, 1),
                    Math.Clamp(x + width, 0, 1),
                    Math.Clamp(y + height / 2, 0, 1),
                    detection.Confidence));
                continue;
            }

            bool ladder = height >= 0.12 && width <= 0.04 && height / width >= 4;
            if (ladder)
            {
                ladders.Add(new MapLadderCandidate(
                    Math.Clamp(x + width / 2, 0, 1),
                    Math.Clamp(y, 0, 1),
                    Math.Clamp(y + height, 0, 1),
                    detection.Confidence));
            }
        }

        return new MapFrameGeometry(platforms, ladders);
    }
}
