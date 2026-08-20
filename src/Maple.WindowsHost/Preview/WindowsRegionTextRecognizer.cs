using Maple.Host.Preview;
using Maple.Host.Recognition;
using System.IO;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Maple.WindowsHost.Preview;

public sealed class WindowsRegionTextRecognizer : IRegionTextRecognizer
{
    private readonly OcrEngine engine;
    private readonly OcrEngine? latinEngine;
    private readonly SemaphoreSlim gate = new(1, 1);

    private WindowsRegionTextRecognizer(OcrEngine engine, OcrEngine? latinEngine)
    {
        this.engine = engine;
        this.latinEngine = latinEngine;
    }

    public static WindowsRegionTextRecognizer? TryCreate()
    {
        try
        {
            var language = new Language("zh-Hans");
            OcrEngine? selected = OcrEngine.IsLanguageSupported(language)
                ? OcrEngine.TryCreateFromLanguage(language)
                : OcrEngine.TryCreateFromUserProfileLanguages();
            OcrEngine? latin = null;
            try
            {
                var english = new Language("en-US");
                if (OcrEngine.IsLanguageSupported(english))
                    latin = OcrEngine.TryCreateFromLanguage(english);
            }
            catch { }
            return selected is null ? null : new WindowsRegionTextRecognizer(selected, latin);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
    }

    public async Task<string> RecognizeAsync(CapturedFrame frame, PixelRegion region, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int width = region.Width;
            int height = region.Height;
            byte[] cropped = new byte[width * height * 4];
            byte[] source = frame.BgraPixels.ToArray();
            for (int row = 0; row < height; row++)
                System.Buffer.BlockCopy(source, (region.Y + row) * frame.Stride + region.X * 4, cropped, row * width * 4, width * 4);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                bool identityRegion = region.Width >= frame.Width * 0.14;
                writer.WriteBytes(BuildBitmap(cropped, width, height, identityRegion ? 3 : 1));
                await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
                writer.DetachStream();
            }
            stream.Seek(0);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(cancellationToken).ConfigureAwait(false);
            OcrResult result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
            string primary = result.Text?.Trim() ?? string.Empty;
            if (latinEngine is null) return primary;
            OcrResult latinResult = await latinEngine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
            string latin = latinResult.Text?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(latin) || string.Equals(primary, latin, StringComparison.Ordinal)
                ? primary
                : $"{primary} {latin}";
        }
        finally { gate.Release(); }
    }

    private static byte[] BuildBitmap(byte[] topDownBgra, int width, int height, int scale)
    {
        int scaledWidth = width * scale;
        int scaledHeight = height * scale;
        int pixelBytes = scaledWidth * scaledHeight * 4;
        using var stream = new MemoryStream(54 + pixelBytes);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B'); writer.Write((byte)'M');
        writer.Write(54 + pixelBytes); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(scaledWidth); writer.Write(scaledHeight);
        writer.Write((short)1); writer.Write((short)32); writer.Write(0);
        writer.Write(pixelBytes); writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
        int rowBytes = width * 4;
        byte[] scaledRow = new byte[scaledWidth * 4];
        for (int row = height - 1; row >= 0; row--)
        {
            for (int sourceX = 0; sourceX < width; sourceX++)
                for (int copy = 0; copy < scale; copy++)
                    System.Buffer.BlockCopy(topDownBgra, row * rowBytes + sourceX * 4, scaledRow, (sourceX * scale + copy) * 4, 4);
            for (int copy = 0; copy < scale; copy++) writer.Write(scaledRow);
        }
        return stream.ToArray();
    }
}

public static class RecognitionProviderFactory
{
    public static IRecognitionProvider Create()
    {
        IRecognitionProvider hud = WindowsRegionTextRecognizer.TryCreate() is { } ocr
        ? new OcrHudRecognitionProvider(ocr)
        : new HeuristicHudRecognitionProvider();
        return OnnxRecognitionProvider.TryCreate(hud);
    }
}
