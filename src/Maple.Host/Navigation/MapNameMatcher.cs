using System.Globalization;
using Maple.Host.Preview;
using Maple.Host.Recognition;

namespace Maple.Host.Navigation;

public sealed record MapNameMatch(bool IsMatch, string? FaultCode);

public static class MapNameMatcher
{
    public static MapNameMatch Match(string expected, string observed)
    {
        string normalizedExpected = Normalize(expected);
        string normalizedObserved = Normalize(observed);
        if (normalizedExpected.Length == 0 || normalizedObserved.Length == 0)
            return Mismatch();

        string expectedDigits = Digits(normalizedExpected);
        string observedDigits = Digits(normalizedObserved);
        if (!string.Equals(expectedDigits, observedDigits, StringComparison.Ordinal))
            return Mismatch();

        string expectedText = Text(normalizedExpected);
        string observedText = Text(normalizedObserved);
        int allowedErrors = Math.Max(1, expectedText.Length / 3);
        bool matched = Math.Abs(expectedText.Length - observedText.Length) <= allowedErrors
            && EditDistance(expectedText, observedText) <= allowedErrors;
        return matched ? new MapNameMatch(true, null) : Mismatch();
    }

    private static string Normalize(string value) => new(
        value.Where(character => char.IsLetterOrDigit(character)).ToArray());

    private static string Digits(string value) => new(
        value.Where(character => char.GetUnicodeCategory(character) == UnicodeCategory.DecimalDigitNumber).ToArray());

    private static string Text(string value) => new(
        value.Where(character => char.GetUnicodeCategory(character) != UnicodeCategory.DecimalDigitNumber).ToArray());

    private static int EditDistance(string first, string second)
    {
        int[] previous = Enumerable.Range(0, second.Length + 1).ToArray();
        int[] current = new int[second.Length + 1];
        for (int firstIndex = 1; firstIndex <= first.Length; firstIndex++)
        {
            current[0] = firstIndex;
            for (int secondIndex = 1; secondIndex <= second.Length; secondIndex++)
            {
                int replacement = previous[secondIndex - 1]
                    + (first[firstIndex - 1] == second[secondIndex - 1] ? 0 : 1);
                current[secondIndex] = Math.Min(
                    Math.Min(previous[secondIndex] + 1, current[secondIndex - 1] + 1),
                    replacement);
            }
            (previous, current) = (current, previous);
        }
        return previous[second.Length];
    }

    private static MapNameMatch Mismatch() => new(false, "MAP_NAME_MISMATCH");
}

public enum MapNameVerification { Pending, Verified, Rejected }

public sealed class MapNameVerificationGate(string expectedName)
{
    private int consecutiveMatches;
    private int consecutiveMismatches;
    private bool verified;

    public MapNameVerification Update(string? observedName)
    {
        if (string.IsNullOrWhiteSpace(observedName))
        {
            consecutiveMatches = 0;
            consecutiveMismatches = 0;
            return MapNameVerification.Pending;
        }

        if (MapNameMatcher.Match(expectedName, observedName).IsMatch)
        {
            consecutiveMismatches = 0;
            if (verified) return MapNameVerification.Verified;
            if (++consecutiveMatches >= 2)
            {
                verified = true;
                return MapNameVerification.Verified;
            }
            return MapNameVerification.Pending;
        }

        consecutiveMatches = 0;
        if (++consecutiveMismatches >= 2)
        {
            verified = false;
            return MapNameVerification.Rejected;
        }
        return MapNameVerification.Pending;
    }
}

public static class MapNameOcrRegion
{
    private static readonly MapMinimapRect LogicalRegion = new(40, 30, 94, 37);

    public static bool TryResolve(CapturedFrame frame, out PixelRegion region)
    {
        region = default!;
        if (!new MapViewportProjection().TryProject(frame, LogicalRegion, out ProjectedMapViewport projected))
            return false;
        region = new PixelRegion(
            projected.MinimapRect.X,
            projected.MinimapRect.Y,
            projected.MinimapRect.Width,
            projected.MinimapRect.Height);
        return true;
    }
}
