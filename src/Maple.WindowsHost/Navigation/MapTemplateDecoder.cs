using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Maple.Host.Navigation;

namespace Maple.WindowsHost.Navigation;

internal static class MapTemplateDecoder
{
    public static IReadOnlyList<BgraTemplate> Decode(string packagePath, MapPackageSnapshot snapshot)
    {
        HashSet<string> approved = snapshot.MonsterTemplates.Select(template => template.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<BgraTemplate> result = [];
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => approved.Contains(entry.FullName)))
        {
            using Stream input = entry.Open();
            PngBitmapDecoder decoder = new(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
            source.CopyPixels(pixels, source.PixelWidth * 4, 0);
            result.Add(new BgraTemplate(entry.FullName, source.PixelWidth, source.PixelHeight, pixels));
        }
        return result;
    }
}
