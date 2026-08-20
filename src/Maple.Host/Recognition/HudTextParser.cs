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
        // The client status bar sits below the chat ticker.  The latter is
        // deliberately excluded because it contains arbitrary player text.
        double top = height >= 900 ? 0.950 : 0.952;
        double regionHeight = height >= 900 ? 0.050 : 0.047;
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
        string value = NormalizeIdentityText(text);
        if (ContainsChatNoise(value)) return new HudIdentity(null, null, null);
        value = value.Replace("猖", "猎", StringComparison.Ordinal);
        value = Regex.Replace(value, @"猎\s*人", "猎人");
        value = Regex.Replace(value, @"(?i)L[VW][\s\.:]*[@#][^\s]*", string.Empty).Trim();
        value = Regex.Replace(value, @"\s*[、丶，,]\s*", "丶");
        Match levelMatch = LevelPattern().Match(value);
        if (!levelMatch.Success && Regex.IsMatch(value, @"(?i)L[VW]"))
            return new HudIdentity(null, null, null);
        int? level = levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out int number) ? number : null;
        string remainder = levelMatch.Success ? value.Remove(levelMatch.Index, levelMatch.Length).Trim() : value;
        string[] parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int nameStart = Array.FindIndex(parts, part => part.Any(character => character <= 127));
        if (nameStart > 0)
            return new HudIdentity(
                string.Concat(parts[nameStart..]),
                level is > 0 ? level : null,
                string.Concat(parts[..nameStart]));
        string? job = value.Contains("猎人", StringComparison.Ordinal) ? "猎人" : null;
        if (parts.All(part => part.All(character => character > 127)))
            return new HudIdentity(null, level is > 0 ? level : null, job);
        return parts.Length switch
        {
            >= 2 => new HudIdentity(parts[^1], level is > 0 ? level : null, string.Join(' ', parts[..^1])),
            1 when parts[0].Any(character => character <= 127) => new HudIdentity(parts[0], level, null),
            _ => new HudIdentity(null, level, null)
        };
    }

    public static string? ExtractLatinName(string? text)
    {
        string value = NormalizeIdentityText(text);
        MatchCollection matches = Regex.Matches(value, @"[A-Za-z][A-Za-z0-9]*(?:丶[A-Za-z0-9]+)+|[A-Za-z][A-Za-z0-9_]{2,}");
        Match? match = matches.Cast<Match>()
            .LastOrDefault(item => !item.Value.Equals("LV", StringComparison.OrdinalIgnoreCase)
                && !item.Value.Equals("HP", StringComparison.OrdinalIgnoreCase)
                && !item.Value.Equals("MP", StringComparison.OrdinalIgnoreCase)
                && !item.Value.Equals("EXP", StringComparison.OrdinalIgnoreCase));
        return match?.Value;
    }

    public static string? ExtractJob(string? text)
    {
        string value = NormalizeIdentityText(text).Replace("猖", "猎", StringComparison.Ordinal);
        value = Regex.Replace(value, @"猎\s*人", "猎人");
        if (value.Contains("猎人", StringComparison.Ordinal)) return "猎人";
        Match? chinese = Regex.Matches(value, @"[\u4e00-\u9fff]{2,6}")
            .Cast<Match>().FirstOrDefault();
        return chinese?.Value;
    }

    public static HudResource ParseResource(string? text)
    {
        string value = NormalizeOcrDigits(text);
        Match match = ResourcePattern().Match(value);
        if (!match.Success) return new HudResource(null, null);
        string currentText = match.Groups[1].Value;
        string maximumText = match.Groups[2].Value;
        if (currentText[0] == '0'
            && maximumText[0] == '1'
            && currentText[1..] == maximumText[1..])
            currentText = maximumText;
        int current = int.Parse(currentText, CultureInfo.InvariantCulture);
        int maximum = int.Parse(maximumText, CultureInfo.InvariantCulture);
        if (maximumText.Length == currentText.Length + 1
            && maximumText.EndsWith('1')
            && maximum / 10 == current)
            maximum /= 10;
        return current <= maximum && maximum is > 0 and <= 10_000_000
            ? new HudResource(current, maximum)
            : new HudResource(null, null);
    }

    private static bool ContainsChatNoise(string value) =>
        value.Contains("金币", StringComparison.Ordinal)
        || value.Contains("加Q", StringComparison.OrdinalIgnoreCase)
        || value.Contains("群", StringComparison.Ordinal)
        || value.Contains("出金", StringComparison.Ordinal)
        || value.Contains("R=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("小时", StringComparison.Ordinal);

    private static string NormalizeIdentityText(string? text)
    {
        string value = Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
        value = value.Replace('．', '.').Replace('。', '.');
        value = Regex.Replace(value, @"\s*[、丶，,]\s*", "丶");
        value = Regex.Replace(value, @"(?<=\d)\s+(?=\d)", string.Empty);
        return Regex.Replace(value, @"(?<=[A-Za-z])\s+(?=[A-Za-z])", string.Empty);
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
