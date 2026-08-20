using System.Globalization;
using System.Text.RegularExpressions;

namespace Maple.Host.Recognition;

public readonly record struct PixelRegion(int X, int Y, int Width, int Height);
public sealed record HudFrameLayout(PixelRegion Identity, PixelRegion HpText, PixelRegion MpText, PixelRegion ExpText);
public sealed record HudIdentity(string? CharacterName, int? Level, string? Job);
public sealed record HudResource(int? Current, int? Maximum);

public static partial class AdaptiveHudLayout
{
    public static HudFrameLayout Resolve(int width, int height)
    {
        if (width < 800 || height < 600) throw new ArgumentOutOfRangeException(nameof(width), "HUD_RESOLUTION_UNSUPPORTED");
        double top = height >= 900 ? 0.93 : 0.952;
        double regionHeight = height >= 900 ? 0.07 : 0.047;
        return new HudFrameLayout(
            Region(width, height, 0.202, top, 0.165, regionHeight),
            Region(width, height, 0.365, top, 0.083, regionHeight),
            Region(width, height, 0.445, top, 0.083, regionHeight),
            Region(width, height, 0.523, top, 0.100, regionHeight));
    }

    private static PixelRegion Region(int width, int height, double x, double y, double w, double h)
    {
        int left = Math.Clamp((int)Math.Round(width * x), 0, width - 1);
        int top = Math.Clamp((int)Math.Round(height * y), 0, height - 1);
        return new PixelRegion(left, top,
            Math.Clamp((int)Math.Round(width * w), 1, width - left),
            Math.Clamp((int)Math.Round(height * h), 1, height - top));
    }
}

public static partial class HudTextParser
{
    [GeneratedRegex(@"(?i)L[VW][\s\.:]*(\d{1,3})")]
    private static partial Regex LevelPattern();
    [GeneratedRegex(@"(\d+)\s*[/\\]\s*(\d+)")]
    private static partial Regex ResourcePattern();
    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*%")]
    private static partial Regex PercentPattern();

    public static HudIdentity ParseIdentity(string? text)
    {
        string value = Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
        value = Regex.Replace(value, @"\s*[、丶，,]\s*", "丶");
        Match levelMatch = LevelPattern().Match(value);
        int? level = levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out int number) ? number : null;
        string remainder = levelMatch.Success ? value.Remove(levelMatch.Index, levelMatch.Length).Trim() : value;
        string[] parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int nameStart = Array.FindIndex(parts, part => part.Any(character => character <= 127));
        if (nameStart > 0)
            return new HudIdentity(
                string.Concat(parts[nameStart..]),
                level is > 0 ? level : null,
                string.Concat(parts[..nameStart]));
        return parts.Length switch
        {
            >= 2 => new HudIdentity(parts[^1], level is > 0 ? level : null, string.Join(' ', parts[..^1])),
            1 => new HudIdentity(parts[0], level, null),
            _ => new HudIdentity(null, level, null)
        };
    }

    public static HudResource ParseResource(string? text)
    {
        string value = NormalizeOcrDigits(text);
        Match match = ResourcePattern().Match(value);
        return match.Success
            ? new HudResource(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            : new HudResource(null, null);
    }

    private static string NormalizeOcrDigits(string? text)
    {
        string value = text ?? string.Empty;
        value = value.Replace('／', '/').Replace('∕', '/');
        value = value.Replace('S', '5').Replace('s', '5')
            .Replace('O', '0').Replace('o', '0')
            .Replace('I', '1').Replace('l', '1').Replace('|', '1');
        value = value.Replace("引", "91").Replace("丨", "1");
        if (value.TrimStart().StartsWith("MP", StringComparison.OrdinalIgnoreCase))
            value = value.Replace("3 91", "991", StringComparison.Ordinal)
                .Replace("3 9l", "991", StringComparison.Ordinal);
        value = Regex.Replace(value, @"(?<=\d)\s+(?=[\d])", string.Empty);
        return value;
    }

    public static double? ParseExperience(string? text)
    {
        string value = (text ?? string.Empty).Replace('。', '.').Replace('．', '.');
        MatchCollection matches = PercentPattern().Matches(value);
        if (matches.Count > 0) return double.Parse(matches[^1].Groups[1].Value, CultureInfo.InvariantCulture);
        MatchCollection decimals = Regex.Matches(value, @"(\d+[\.,]\d+)");
        return decimals.Count == 0 ? null : double.Parse(decimals[^1].Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
    }
}
