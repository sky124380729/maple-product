namespace Maple.Host.Recognition;

public sealed record YoloDetection(string ClassName, double Confidence, double X, double Y, double Width, double Height);

public static class YoloTensorDecoder
{
    public static IReadOnlyList<YoloDetection> DecodeChannelsFirst(
        IReadOnlyList<float> tensor, IReadOnlyList<string> classes, int candidates,
        double confidenceThreshold, double nmsThreshold, int inputWidth, int inputHeight)
    {
        int channels = 4 + classes.Count;
        if (tensor.Count != channels * candidates) throw new InvalidDataException("MODEL_OUTPUT_SHAPE_INVALID");
        var decoded = new List<YoloDetection>();
        for (int candidate = 0; candidate < candidates; candidate++)
        {
            float Read(int channel) => tensor[channel * candidates + candidate];
            int classIndex = 0;
            double confidence = Read(4);
            for (int index = 1; index < classes.Count; index++)
            {
                double score = Read(4 + index);
                if (score > confidence) { confidence = score; classIndex = index; }
            }
            if (confidence < confidenceThreshold) continue;
            double width = Normalize(Read(2), inputWidth);
            double height = Normalize(Read(3), inputHeight);
            double left = Math.Clamp(Normalize(Read(0), inputWidth) - width / 2, 0, 1);
            double top = Math.Clamp(Normalize(Read(1), inputHeight) - height / 2, 0, 1);
            double right = Math.Clamp(left + width, 0, 1);
            double bottom = Math.Clamp(top + height, 0, 1);
            if (right > left && bottom > top)
                decoded.Add(new YoloDetection(classes[classIndex], confidence, left, top, right - left, bottom - top));
        }
        var kept = new List<YoloDetection>();
        foreach (YoloDetection candidate in decoded.OrderByDescending(item => item.Confidence))
        {
            if (kept.Any(item => item.ClassName == candidate.ClassName && IntersectionOverUnion(item, candidate) > nmsThreshold)) continue;
            kept.Add(candidate);
        }
        return kept;
    }

    private static double Normalize(double value, int extent) => value > 1 ? value / extent : value;

    private static double IntersectionOverUnion(YoloDetection first, YoloDetection second)
    {
        double left = Math.Max(first.X, second.X);
        double top = Math.Max(first.Y, second.Y);
        double right = Math.Min(first.X + first.Width, second.X + second.Width);
        double bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);
        double intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        double union = first.Width * first.Height + second.Width * second.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}
