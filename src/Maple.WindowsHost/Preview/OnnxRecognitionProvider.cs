using System.Security.Cryptography;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maple.Host.Preview;
using Maple.Host.Recognition;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Maple.WindowsHost.Preview;

internal sealed class RecognitionModelManifest
{
    public int SchemaVersion { get; init; }
    public string ModelFile { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public int InputWidth { get; init; }
    public int InputHeight { get; init; }
    public double ConfidenceThreshold { get; init; }
    public double NmsThreshold { get; init; }
    public string[] Classes { get; init; } = [];
    public Dictionary<string, string> ClassRoles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OnnxRecognitionProvider : IRecognitionProvider, IAsyncDisposable
{
    private readonly IRecognitionProvider hud;
    private readonly RecognitionModelManifest manifest;
    private readonly InferenceSession session;
    private readonly RecognitionTargetStabilizer dropStabilizer = new();
    private readonly SpriteSceneRecognizer? scene;

    private OnnxRecognitionProvider(IRecognitionProvider hud, RecognitionModelManifest manifest, InferenceSession session, SpriteSceneRecognizer? scene)
    {
        this.hud = hud;
        this.manifest = manifest;
        this.session = session;
        this.scene = scene;
    }

    public static IRecognitionProvider TryCreate(IRecognitionProvider hud)
    {
        string manifestPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Maple", "models", "active", "manifest.json");
        try
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            RecognitionModelManifest? manifest = JsonSerializer.Deserialize<RecognitionModelManifest>(File.ReadAllText(manifestPath), options);
            if (manifest is null || manifest.SchemaVersion != 2 || manifest.InputWidth <= 0 || manifest.InputHeight <= 0 || manifest.Classes.Length == 0)
                return hud;
            string modelPath = Path.GetFullPath(manifest.ModelFile);
            if (!File.Exists(modelPath)) return hud;
            using FileStream stream = File.OpenRead(modelPath);
            string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase)) return hud;
            return new OnnxRecognitionProvider(hud, manifest, new InferenceSession(modelPath), SpriteSceneRecognizer.TryCreate(modelPath));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return hud; }
    }

    public async Task<RecognitionAnalysis> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        RecognitionAnalysis baseResult = await hud.AnalyzeAsync(frame, cancellationToken).ConfigureAwait(false);
        string inputName = session.InputMetadata.Keys.Single();
        var characters = new List<RecognitionTarget>();
        var monsters = new List<RecognitionTarget>();
        var drops = new List<RecognitionTarget>();
        // The checkpoint was trained on complete game screenshots. A single
        // full-frame pass avoids running eight overlapping inferences on every
        // preview frame; the sprite fallback handles small targets separately.
        IEnumerable<RecognitionTile> tiles = [new RecognitionTile(0, 0, frame.Width, frame.Height)];
        foreach (RecognitionTile tileInfo in tiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CapturedFrame tile = CapturedFrameCropper.Crop(frame, tileInfo.X, tileInfo.Y, tileInfo.Width, tileInfo.Height);
            // The packaged checkpoint is exported with RGB channel order.
            // Feeding BGR suppresses mob scores to near zero on the real
            // client frame, so keep the capture's RGB semantic order here.
            float[] input = ToNchw(tile, manifest.InputWidth, manifest.InputHeight);
            var tensor = new DenseTensor<float>(input, [1, 3, manifest.InputHeight, manifest.InputWidth]);
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = session.Run(
                [NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
            Tensor<float> output = outputs.Single().AsTensor<float>();
            int[] dimensions = output.Dimensions.ToArray();
            int channels = 4 + manifest.Classes.Length;
            int candidates = dimensions.Length == 3 && dimensions[1] == channels
                ? dimensions[2]
                : throw new InvalidDataException("MODEL_OUTPUT_SHAPE_INVALID");
            IReadOnlyList<YoloDetection> detections = YoloTensorDecoder.DecodeChannelsFirst(
                output.ToArray(), manifest.Classes, candidates, 0.10,
                manifest.NmsThreshold, manifest.InputWidth, manifest.InputHeight);
            AddDetections(detections, tileInfo.X, tileInfo.Y, tile, characters, monsters, drops);
        }
        SceneRecognitionResult sceneResult = (scene is not null && (monsters.Count == 0 || characters.Count == 0))
            ? scene.Analyze(frame)
            : new SceneRecognitionResult(null, [], []);
        IReadOnlyList<RecognitionTarget> filteredCharacters = SuppressOverlaps(characters);
        RecognitionTarget? selfCandidate = filteredCharacters.OrderBy(item => Math.Abs((item.X + item.Width / 2) - frame.Width / 2d)).FirstOrDefault();
        IEnumerable<RecognitionTarget> sceneMonsters = monsters.Count == 0 ? sceneResult.Monsters : [];
        IReadOnlyList<RecognitionTarget> filteredMonsters = SuppressOverlaps(
            monsters.Concat(sceneMonsters).Where(item => RecognitionTargetFilter.IsPlausibleMonster(item, selfCandidate is null
                ? null
                : new SelfObservation(selfCandidate.X, selfCandidate.Y, selfCandidate.Width,
                    selfCandidate.Height, null, selfCandidate.Confidence))).ToList());
        IReadOnlyList<RecognitionTarget> plausibleDrops = drops
            .Where(item => RecognitionTargetFilter.IsPlausibleDrop(item, selfCandidate is null
                ? null
                : new SelfObservation(selfCandidate.X, selfCandidate.Y, selfCandidate.Width,
                    selfCandidate.Height, null, selfCandidate.Confidence)))
            .ToArray();
        IReadOnlyList<RecognitionTarget> filteredDrops = dropStabilizer.Update(
            SuppressOverlaps(plausibleDrops.ToList()), frame.Sequence);
        SelfObservation? self = selfCandidate is null
            ? sceneResult.Self
            : new SelfObservation(selfCandidate.X, selfCandidate.Y, selfCandidate.Width, selfCandidate.Height, null, selfCandidate.Confidence);
        IReadOnlyList<RecognitionTarget> otherPlayers = filteredCharacters
            .Where(item => !ReferenceEquals(item, selfCandidate))
            .Select(item => item with { Kind = "player" })
            .Concat(sceneResult.OtherPlayers)
            .ToArray();
        return baseResult with { Monsters = filteredMonsters, Drops = filteredDrops, OtherPlayers = otherPlayers, Self = self };
    }

    private void AddDetections(
        IReadOnlyList<YoloDetection> detections, int offsetX, int offsetY, CapturedFrame tile,
        List<RecognitionTarget> characters, List<RecognitionTarget> monsters,
        List<RecognitionTarget> drops)
    {
        foreach (YoloDetection detection in detections)
        {
            if (!manifest.ClassRoles.TryGetValue(detection.ClassName, out string? role)) continue;
            // The packaged model was trained on downscaled screenshots and
            // emits small-map mobs around 0.10-0.30 confidence. Keep the
            // character threshold conservative, but do not discard a mob
            // solely because the manifest's generic 0.60 threshold is too
            // high for this class.
            double minimumConfidence = role switch
            {
                "characterCandidate" => 0.10,
                "monster" => Math.Min(manifest.ConfidenceThreshold, 0.10),
                _ => manifest.ConfidenceThreshold
            };
            if (detection.Confidence < minimumConfidence) continue;
            var target = new RecognitionTarget(
                offsetX + detection.X * tile.Width, offsetY + detection.Y * tile.Height,
                detection.Width * tile.Width, detection.Height * tile.Height,
                role == "monster" ? "monster" : role == "drop" ? "drop" : "character",
                detection.Confidence);
            if (role == "monster") monsters.Add(target);
            else if (role == "drop") drops.Add(target);
            else if (role == "characterCandidate") characters.Add(target);
        }
    }

    private static IReadOnlyList<RecognitionTarget> SuppressOverlaps(List<RecognitionTarget> candidates)
    {
        var kept = new List<RecognitionTarget>();
        foreach (RecognitionTarget candidate in candidates.OrderByDescending(item => item.Confidence))
        {
            if (kept.Any(item => IoU(item, candidate) > 0.45)) continue;
            kept.Add(candidate);
        }
        return kept;
    }

    private static double IoU(RecognitionTarget first, RecognitionTarget second)
    {
        double left = Math.Max(first.X, second.X);
        double top = Math.Max(first.Y, second.Y);
        double right = Math.Min(first.X + first.Width, second.X + second.Width);
        double bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);
        double intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        double union = first.Width * first.Height + second.Width * second.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    public ValueTask DisposeAsync()
    {
        session.Dispose();
        return ValueTask.CompletedTask;
    }

    private static float[] ToNchw(CapturedFrame frame, int width, int height)
    {
        float[] output = new float[3 * width * height];
        ReadOnlySpan<byte> pixels = frame.BgraPixels.Span;
        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(frame.Height - 1, y * frame.Height / height);
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(frame.Width - 1, x * frame.Width / width);
                int source = sourceY * frame.Stride + sourceX * 4;
                int target = y * width + x;
                output[target] = pixels[source + 2] / 255f;
                output[width * height + target] = pixels[source + 1] / 255f;
                output[2 * width * height + target] = pixels[source] / 255f;
            }
        }
        return output;
    }

}
