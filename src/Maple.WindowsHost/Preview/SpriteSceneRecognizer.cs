using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Maple.Host.Preview;
using Maple.Host.Recognition;

namespace Maple.WindowsHost.Preview;

internal sealed record SceneRecognitionResult(
    SelfObservation? Self,
    IReadOnlyList<RecognitionTarget> Monsters,
    IReadOnlyList<RecognitionTarget> OtherPlayers);

/// <summary>
/// Small, deterministic fallback for the packaged YOLO model. The auxiliary
/// client assets are animation sprites, so matching a sparse opaque-pixel
/// signature is more useful here than lowering a model threshold until brick
/// textures become false monsters.
/// </summary>
internal sealed class SpriteSceneRecognizer
{
    private readonly IReadOnlyList<SpriteTemplate> templates;
    private readonly object gate = new();
    private long lastFrame = long.MinValue;
    private SceneRecognitionResult cached = new(null, [], []);

    private SpriteSceneRecognizer(IReadOnlyList<SpriteTemplate> templates) => this.templates = templates;

    public static SpriteSceneRecognizer? TryCreate(string modelPath)
    {
        try
        {
            string? weights = Path.GetDirectoryName(modelPath);
            string? root = weights is null ? null : Directory.GetParent(weights)?.FullName;
            string? monsterRoot = root is null ? null : Path.Combine(root, "MapleStoryAutoLevelUp", "monster");
            if (monsterRoot is null || !Directory.Exists(monsterRoot)) return null;
            var templates = new List<SpriteTemplate>();
            foreach (string folder in Directory.EnumerateDirectories(monsterRoot))
            {
                string? file = Directory.EnumerateFiles(folder, "*.png").OrderBy(path => path).FirstOrDefault();
                if (file is not null && SpriteTemplate.TryLoad(file) is { } template) templates.Add(template);
            }
            return templates.Count == 0 ? null : new SpriteSceneRecognizer(templates);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
    }

    public SceneRecognitionResult Analyze(CapturedFrame frame)
    {
        lock (gate)
        {
            if (frame.Sequence == lastFrame) return cached;
            if (lastFrame != long.MinValue && frame.CapturedAtMonoMs - cachedFrameAt < 750)
                return cached;
            cached = AnalyzeCore(frame);
            cachedFrameAt = frame.CapturedAtMonoMs;
            lastFrame = frame.Sequence;
            return cached;
        }
    }

    private long cachedFrameAt = long.MinValue;

    private SceneRecognitionResult AnalyzeCore(CapturedFrame frame)
    {
        IReadOnlyList<RecognitionTarget> monsters = MatchMonsters(frame);
        IReadOnlyList<RecognitionTarget> labels = FindNameplates(frame);
        if (labels.Count == 0) return new SceneRecognitionResult(null, monsters, []);
        RecognitionTarget selfLabel = labels.OrderBy(label =>
            Math.Abs(label.X + label.Width / 2d - frame.Width / 2d)).First();
        SelfObservation self = new(
            Math.Max(0, selfLabel.X - selfLabel.Width * 0.15),
            Math.Max(0, selfLabel.Y - 66),
            Math.Max(18, selfLabel.Width * 0.70),
            62,
            null,
            0.72);
        IReadOnlyList<RecognitionTarget> otherPlayers = labels
            .Where(label => !ReferenceEquals(label, selfLabel))
            .Select(label => label with { Kind = "player", Confidence = 0.65 })
            .ToArray();
        return new SceneRecognitionResult(self, monsters, otherPlayers);
    }

    private IReadOnlyList<RecognitionTarget> MatchMonsters(CapturedFrame frame)
    {
        var found = new List<RecognitionTarget>();
        double scale = Math.Clamp(frame.Width / 1366d, 0.85, 1.70);
        foreach (SpriteTemplate template in templates)
        {
            int scaledWidth = Math.Max(1, (int)Math.Round(template.Width * scale));
            int scaledHeight = Math.Max(1, (int)Math.Round(template.Height * scale));
            if (scaledWidth >= frame.Width || scaledHeight >= frame.Height) continue;
            int maxY = Math.Min((int)(frame.Height * 0.86), frame.Height - scaledHeight);
            int minY = Math.Min(maxY, Math.Max(0, (int)(frame.Height * 0.12)));
            for (int y = minY; y <= maxY; y += 6)
            {
                for (int x = 0; x <= frame.Width - scaledWidth; x += 6)
                {
                    if (x < frame.Width * 0.18 && y < frame.Height * 0.28) continue;
                    double score = template.Score(frame, x, y, scale);
                    if (score < 0.42) continue;
                    if (!HasGroundSupport(frame, x, y, scaledWidth, scaledHeight)) continue;
                    var candidate = new RecognitionTarget(x, y, scaledWidth, scaledHeight, "monster", score);
                    if (found.Any(existing => IoU(existing, candidate) > 0.25)) continue;
                    found.Add(candidate);
                    if (found.Count >= 12) return found;
                }
            }
        }
        return found.OrderByDescending(item => item.Confidence).ToArray();
    }

    private static bool HasGroundSupport(CapturedFrame frame, int x, int y, int width, int height)
    {
        int row = Math.Min(frame.Height - 1, y + height + 3);
        int start = Math.Max(0, x + width / 8);
        int end = Math.Min(frame.Width, x + width - width / 8);
        int supported = 0;
        int total = Math.Max(1, end - start);
        ReadOnlySpan<byte> pixels = frame.BgraPixels.Span;
        for (int px = start; px < end; px += 2)
        {
            int offset = row * frame.Stride + px * 4;
            byte b = pixels[offset], g = pixels[offset + 1], r = pixels[offset + 2];
            if ((r > 65 && r > g + 12 && g > b + 8) || (r < 75 && g < 75 && b < 75)) supported++;
        }
        return supported >= total * 0.30;
    }

    private static IReadOnlyList<RecognitionTarget> FindNameplates(CapturedFrame frame)
    {
        bool[] marked = new bool[frame.Width * frame.Height];
        var labels = new List<RecognitionTarget>();
        ReadOnlySpan<byte> pixels = frame.BgraPixels.Span;
        for (int y = (int)(frame.Height * 0.20); y < frame.Height * 0.84; y += 2)
        for (int x = 0; x < frame.Width; x += 2)
        {
            int index = y * frame.Width + x;
            if (marked[index] || !IsNameplatePixel(pixels, frame.Stride, x, y)) continue;
            int left = x, right = x, top = y, bottom = y, count = 0;
            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue((x, y));
            marked[index] = true;
            while (queue.Count > 0)
            {
                (int px, int py) = queue.Dequeue();
                count++;
                left = Math.Min(left, px); right = Math.Max(right, px);
                top = Math.Min(top, py); bottom = Math.Max(bottom, py);
                foreach ((int nx, int ny) in new[] { (px + 2, py), (px - 2, py), (px, py + 2), (px, py - 2) })
                {
                    if (nx < 0 || ny < 0 || nx >= frame.Width || ny >= frame.Height) continue;
                    int ni = ny * frame.Width + nx;
                    if (marked[ni] || !IsNameplatePixel(pixels, frame.Stride, nx, ny)) continue;
                    marked[ni] = true;
                    queue.Enqueue((nx, ny));
                }
            }
            int width = right - left + 2;
            int height = bottom - top + 2;
            if (count >= 10 && width is >= 18 and <= 180 && height is >= 4 and <= 26)
                labels.Add(new RecognitionTarget(left, top, width, height, "player", 0.72));
        }
        return SuppressOverlaps(labels);
    }

    private static bool IsNameplatePixel(ReadOnlySpan<byte> pixels, int stride, int x, int y)
    {
        int offset = y * stride + x * 4;
        byte b = pixels[offset], g = pixels[offset + 1], r = pixels[offset + 2];
        return b > 125 && g > 80 && b - r > 55 && g - r > 35;
    }

    private static IReadOnlyList<RecognitionTarget> SuppressOverlaps(List<RecognitionTarget> values)
    {
        var kept = new List<RecognitionTarget>();
        foreach (RecognitionTarget candidate in values.OrderByDescending(item => item.Confidence))
        {
            if (kept.Any(previous => IoU(previous, candidate) > 0.45)) continue;
            kept.Add(candidate);
        }
        return kept;
    }

    private static double IoU(RecognitionTarget first, RecognitionTarget second)
    {
        double left = Math.Max(first.X, second.X), top = Math.Max(first.Y, second.Y);
        double right = Math.Min(first.X + first.Width, second.X + second.Width);
        double bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);
        double intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        double union = first.Width * first.Height + second.Width * second.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    private sealed class SpriteTemplate
    {
        private readonly IReadOnlyList<Sample> samples;
        public int Width { get; }
        public int Height { get; }
        private SpriteTemplate(int width, int height, IReadOnlyList<Sample> samples)
            => (Width, Height, this.samples) = (width, height, samples);

        public static SpriteTemplate? TryLoad(string path)
        {
            try
            {
                var decoder = new PngBitmapDecoder(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
                int width = source.PixelWidth, height = source.PixelHeight;
                byte[] pixels = new byte[width * height * 4];
                source.CopyPixels(pixels, width * 4, 0);
                var opaque = new List<Sample>();
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int offset = (y * width + x) * 4;
                    if (pixels[offset + 3] < 100) continue;
                    byte b = pixels[offset], g = pixels[offset + 1], r = pixels[offset + 2];
                    opaque.Add(new Sample(x, y, b, g, r, g > r + 8 && g > b + 8));
                }
                if (opaque.Count < 20) return null;
                int step = Math.Max(1, opaque.Count / 48);
                return new SpriteTemplate(width, height, opaque.Where((_, i) => i % step == 0).Take(40).ToArray());
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
        }

        public double Score(CapturedFrame frame, int x, int y, double scale = 1)
        {
            ReadOnlySpan<byte> pixels = frame.BgraPixels.Span;
            int matched = 0;
            int matchedDistinctive = 0;
            foreach (Sample sample in samples)
            {
                int sampleX = Math.Min(frame.Width - 1, x + (int)Math.Round(sample.X * scale));
                int sampleY = Math.Min(frame.Height - 1, y + (int)Math.Round(sample.Y * scale));
                int offset = sampleY * frame.Stride + sampleX * 4;
                int distance = Math.Abs(pixels[offset] - sample.B)
                    + Math.Abs(pixels[offset + 1] - sample.G)
                    + Math.Abs(pixels[offset + 2] - sample.R);
                if (distance <= 100)
                {
                    matched++;
                    if (sample.Distinctive) matchedDistinctive++;
                }
            }
            int requiredDistinctive = Math.Min(4, samples.Count / 20);
            if (matchedDistinctive < requiredDistinctive) return 0;
            return matched / (double)samples.Count;
        }

        private readonly record struct Sample(int X, int Y, byte B, byte G, byte R, bool Distinctive);
    }
}
