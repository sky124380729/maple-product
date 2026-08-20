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

    private OnnxRecognitionProvider(IRecognitionProvider hud, RecognitionModelManifest manifest, InferenceSession session)
    {
        this.hud = hud;
        this.manifest = manifest;
        this.session = session;
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
            return new OnnxRecognitionProvider(hud, manifest, new InferenceSession(modelPath));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return hud; }
    }

    public async Task<RecognitionAnalysis> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        RecognitionAnalysis baseResult = await hud.AnalyzeAsync(frame, cancellationToken).ConfigureAwait(false);
        float[] input = ToNchw(frame, manifest.InputWidth, manifest.InputHeight);
        var tensor = new DenseTensor<float>(input, [1, 3, manifest.InputHeight, manifest.InputWidth]);
        string inputName = session.InputMetadata.Keys.Single();
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = session.Run(
            [NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
        Tensor<float> output = outputs.Single().AsTensor<float>();
        int[] dimensions = output.Dimensions.ToArray();
        int channels = 4 + manifest.Classes.Length;
        int candidates = dimensions.Length == 3 && dimensions[1] == channels
            ? dimensions[2]
            : throw new InvalidDataException("MODEL_OUTPUT_SHAPE_INVALID");
        IReadOnlyList<YoloDetection> detections = YoloTensorDecoder.DecodeChannelsFirst(
            output.ToArray(), manifest.Classes, candidates, manifest.ConfidenceThreshold,
            manifest.NmsThreshold, manifest.InputWidth, manifest.InputHeight);

        var characters = new List<RecognitionTarget>();
        var monsters = new List<RecognitionTarget>();
        var drops = new List<RecognitionTarget>();
        foreach (YoloDetection detection in detections)
        {
            if (!manifest.ClassRoles.TryGetValue(detection.ClassName, out string? role)) continue;
            var target = new RecognitionTarget(
                detection.X * frame.Width, detection.Y * frame.Height,
                detection.Width * frame.Width, detection.Height * frame.Height,
                role == "monster" ? "monster" : role == "drop" ? "drop" : "character",
                detection.Confidence);
            if (role == "monster") monsters.Add(target);
            else if (role == "drop") drops.Add(target);
            else if (role == "characterCandidate") characters.Add(target);
        }
        RecognitionTarget? selfCandidate = characters.OrderBy(item => Math.Abs((item.X + item.Width / 2) - frame.Width / 2d)).FirstOrDefault();
        SelfObservation? self = selfCandidate is null ? null : new SelfObservation(
            selfCandidate.X, selfCandidate.Y, selfCandidate.Width, selfCandidate.Height, null, selfCandidate.Confidence);
        IReadOnlyList<RecognitionTarget> otherPlayers = selfCandidate is null
            ? []
            : characters.Where(item => !ReferenceEquals(item, selfCandidate)).Select(item => item with { Kind = "player" }).ToArray();
        return baseResult with { Monsters = monsters, Drops = drops, OtherPlayers = otherPlayers, Self = self };
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
