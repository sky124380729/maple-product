using System.Collections.Immutable;
using Maple.Host.Navigation;
using Maple.Host.Preview;

namespace Maple.Host.Tests.Navigation;

public sealed class MapSignatureMatcherTests
{
    [Fact]
    public void Matches_platform_and_ladder_inside_fixed_roi()
    {
        MapPackageSnapshot map = Map();
        CapturedFrame frame = Frame(120, 80, 1, 10, true);

        MapSignatureMatch match = new MapSignatureMatcher().Match(frame, map);

        Assert.True(match.IsMatch);
        Assert.InRange(match.Confidence, 0.7, 1);
        Assert.Null(match.FaultCode);
    }

    [Fact]
    public void Rejects_roi_outside_viewport()
    {
        MapPackageSnapshot map = Map() with { MinimapRect = new MapMinimapRect(100, 70, 100, 60) };

        MapSignatureMatch match = new MapSignatureMatcher().Match(Frame(120, 80, 1, 10, true), map);

        Assert.False(match.IsMatch);
        Assert.Equal("MAP_VIEWPORT_MISMATCH", match.FaultCode);
    }

    [Fact]
    public void Gate_arms_after_five_new_matches_and_rejects_three_mismatches()
    {
        NavigationLocalizationGate gate = new();
        NavigationLocalization latest = default!;
        for (int sequence = 1; sequence <= 5; sequence++)
            latest = gate.Update(Localization(sequence, sequence * 10, matched: true));
        Assert.True(latest.MapMatched);

        for (int sequence = 6; sequence <= 8; sequence++)
            latest = gate.Update(Localization(sequence, sequence * 10, matched: false));
        Assert.False(latest.MapMatched);
        Assert.Equal("MAP_MISMATCH", latest.FaultCode);
    }

    [Fact]
    public void Gate_rejects_stale_last_match()
    {
        NavigationLocalizationGate gate = new();
        for (int sequence = 1; sequence <= 5; sequence++)
            gate.Update(Localization(sequence, sequence * 10, matched: true));

        NavigationLocalization stale = gate.Update(Localization(6, 600, matched: false));

        Assert.False(stale.MapMatched);
        Assert.Equal("OBSERVATION_STALE", stale.FaultCode);
    }

    private static NavigationLocalization Localization(long sequence, long time, bool matched) =>
        new(sequence, time, matched, matched ? 1 : 0, new MapPoint(50, 20), 0, null);

    internal static MapPackageSnapshot Map() => new(
        "Test", new MapMinimapRect(5, 5, 100, 60), "manual",
        new MapPackageThresholds(0.15, 0.5, 140, 70),
        [new MapPlatform(0, 10, 90, 20)],
        [new MapLadder(0, 50, 20, 50, [0])],
        [], [], [], [], [], [], [], true, []);

    internal static CapturedFrame Frame(int width, int height, long sequence, int markerX, bool structure, int markerY = 20)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
        if (structure)
        {
            for (int x = 15; x <= 95; x++) Set(pixels, width, x, 25, 30, 180, 40);
            for (int y = 25; y <= 55; y++) Set(pixels, width, 55, y, 130, 130, 130);
        }
        for (int y = 5 + markerY - 2; y <= 5 + markerY + 2; y++)
        for (int x = 5 + markerX - 2; x <= 5 + markerX + 2; x++)
            Set(pixels, width, x, y, 20, 230, 245);
        return new CapturedFrame(width, height, width * 4, pixels, sequence * 10, sequence);
    }

    private static void Set(byte[] pixels, int width, int x, int y, byte b, byte g, byte r)
    {
        int offset = (y * width + x) * 4;
        pixels[offset] = b;
        pixels[offset + 1] = g;
        pixels[offset + 2] = r;
    }
}
