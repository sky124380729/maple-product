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
        return new HudFrameLayout(
            Region(width, height, 0.202, 0.952, 0.165, 0.047),
            Region(width, height, 0.365, 0.952, 0.083, 0.047),
            Region(width, height, 0.445, 0.952, 0.083, 0.047),
            Region(width, height, 0.523, 0.952, 0.100, 0.047));
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
        Match levelMatch = LevelPattern().Match(value);
        int? level = levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out int number) ? number : null;
        string remainder = levelMatch.Success ? value.Remove(levelMatch.Index, levelMatch.Length).Trim() : value;
        string[] parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            >= 2 => new HudIdentity(parts[^1], level, string.Join(' ', parts[..^1])),
            1 => new HudIdentity(parts[0], level, null),
            _ => new HudIdentity(null, level, null)
        };
    }

    public static HudResource ParseResource(string? text)
    {
        Match match = ResourcePattern().Match(text ?? string.Empty);
        return match.Success
            ? new HudResource(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            : new HudResource(null, null);
    }

    public static double? ParseExperience(string? text)
    {
        MatchCollection matches = PercentPattern().Matches(text ?? string.Empty);
        return matches.Count == 0 ? null : double.Parse(matches[^1].Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
