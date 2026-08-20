namespace Maple.Host.Recognition;

public sealed record RecognitionOverlayBox(
    string Kind, double X, double Y, double Width, double Height, double Confidence);

public static class RecognitionOverlayLayout
{
    public static IReadOnlyList<RecognitionOverlayBox> Create(
        RecognitionSnapshot snapshot,
        int frameWidth,
        int frameHeight,
        double viewportWidth,
        double viewportHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0) return [];
        double scale = Math.Min(viewportWidth / frameWidth, viewportHeight / frameHeight);
        double offsetX = (viewportWidth - frameWidth * scale) / 2;
        double offsetY = (viewportHeight - frameHeight * scale) / 2;
        var boxes = new List<RecognitionOverlayBox>();
        if (snapshot.Self is { } self)
            Add(boxes, "self", self.X, self.Y, self.Width, self.Height, self.Confidence, scale, offsetX, offsetY);
        foreach (RecognitionTarget monster in snapshot.Monsters)
            Add(boxes, "monster", monster.X, monster.Y, monster.Width, monster.Height, monster.Confidence, scale, offsetX, offsetY);
        return boxes;
    }

    private static void Add(
        List<RecognitionOverlayBox> boxes, string kind,
        double x, double y, double width, double height, double confidence,
        double scale, double offsetX, double offsetY) =>
        boxes.Add(new RecognitionOverlayBox(
            kind, offsetX + x * scale, offsetY + y * scale,
            width * scale, height * scale, confidence));
}
