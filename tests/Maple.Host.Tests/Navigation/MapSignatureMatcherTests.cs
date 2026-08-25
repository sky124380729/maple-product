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

        MapSignatureMatch match = Matcher().Match(frame, map);

        Assert.True(match.IsMatch);
        Assert.InRange(match.Confidence, 0.7, 1);
        Assert.Null(match.FaultCode);
    }

    [Fact]
    public void Rejects_roi_outside_viewport()
    {
        MapPackageSnapshot map = Map() with { MinimapRect = new MapMinimapRect(100, 70, 100, 60) };

        MapSignatureMatch match = Matcher().Match(Frame(120, 80, 1, 10, true), map);

        Assert.False(match.IsMatch);
        Assert.Equal("MAP_VIEWPORT_MISMATCH", match.FaultCode);
    }

    [Fact]
    public void Matches_uniformly_scaled_physical_frame_and_returns_logical_coordinates()
    {
        MapPackageSnapshot map = Map();
        CapturedFrame scaled = Scale(Frame(120, 80, 4, 50, true), 1.5);

        MapSignatureMatch match = Matcher().Match(scaled, map);
        NavigationLocalization localization = new MinimapLocalizer(Projection()).Observe(
            scaled,
            map,
            NavigationTraversal.None);

        Assert.True(match.IsMatch);
        Assert.NotNull(localization.Self);
        Assert.InRange(localization.Self!.X, 48.5, 51.5);
        Assert.InRange(localization.Self.Y, 18.5, 21.5);
        Assert.Equal(0, localization.PlatformId);
    }

    [Fact]
    public void Calibrates_small_global_minimap_offset_and_returns_package_coordinates()
    {
        MapPackageSnapshot map = Map();
        CapturedFrame shifted = Shift(Frame(120, 80, 4, 50, true), 5, -3);

        MapSignatureMatch match = Matcher().Match(shifted, map);
        NavigationLocalization localization = new MinimapLocalizer(Projection()).Observe(
            shifted,
            map,
            NavigationTraversal.None);

        Assert.True(match.IsMatch);
        Assert.InRange(match.LogicalOffsetX, 4, 6);
        Assert.InRange(match.LogicalOffsetY, -4, -2);
        Assert.NotNull(localization.Self);
        Assert.InRange(localization.Self!.X, 48.5, 51.5);
        Assert.InRange(localization.Self.Y, 18.5, 21.5);
        Assert.Equal(0, localization.PlatformId);
    }

    [Fact]
    public void Projects_legacy_window_relative_roi_into_client_frame()
    {
        MapPackageSnapshot map = Map() with
        {
            MinimapRect = new MapMinimapRect(5, 37, 100, 60),
            MinimapReferenceTopInset = 32
        };

        NavigationLocalization localization = new MinimapLocalizer(Projection()).Observe(
            Frame(120, 80, 4, 50, true),
            map,
            NavigationTraversal.None);

        Assert.True(localization.MapMatched);
        Assert.Equal(new MapPoint(50, 20), localization.Self);
        Assert.Equal(0, localization.PlatformId);
    }

    [Fact]
    public void Gate_arms_after_five_new_matches_and_rejects_three_mismatches()
    {
        NavigationLocalizationGate gate = new();
        NavigationLocalization latest = default!;
        for (int sequence = 1; sequence < 5; sequence++)
        {
            latest = gate.Update(Localization(sequence, sequence * 10, matched: true));
            Assert.False(latest.MapMatched);
            Assert.Equal("MAP_VALIDATION_PENDING", latest.FaultCode);
        }

        latest = gate.Update(Localization(5, 50, matched: true));
        Assert.True(latest.MapMatched);
        Assert.Null(latest.FaultCode);

        for (int sequence = 6; sequence < 8; sequence++)
        {
            latest = gate.Update(Localization(sequence, sequence * 10, matched: false));
            Assert.False(latest.MapMatched);
            Assert.Equal("MAP_VALIDATION_PENDING", latest.FaultCode);
        }

        latest = gate.Update(Localization(8, 80, matched: false));
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

    [Fact]
    public void Gate_keeps_preflight_pending_when_map_matches_but_marker_temporarily_disappears()
    {
        NavigationLocalizationGate gate = new();
        gate.Update(Localization(1, 10, matched: true));
        gate.Update(Localization(2, 20, matched: true));

        NavigationLocalization missingMarker = gate.Update(new NavigationLocalization(
            3,
            30,
            true,
            0.8,
            null,
            null,
            "SELF_NOT_LOCALIZED"));

        Assert.False(missingMarker.MapMatched);
        Assert.Equal("MAP_VALIDATION_PENDING", missingMarker.FaultCode);
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
        int markerCenterY = markerY - 7;
        for (int y = 5 + markerCenterY - 2; y <= 5 + markerCenterY + 2; y++)
        for (int x = 5 + markerX - 2; x <= 5 + markerX + 2; x++)
            Set(pixels, width, x, y, 20, 230, 245);
        return new CapturedFrame(width, height, width * 4, pixels, sequence * 10, sequence);
    }

    internal static MapViewportProjection Projection() => new(120, 80);

    internal static MapSignatureMatcher Matcher() => new(Projection());

    private static CapturedFrame Scale(CapturedFrame source, double scale)
    {
        int width = (int)Math.Round(source.Width * scale);
        int height = (int)Math.Round(source.Height * scale);
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int sourceX = Math.Min(source.Width - 1, (int)(x / scale));
            int sourceY = Math.Min(source.Height - 1, (int)(y / scale));
            source.BgraPixels.Span.Slice(sourceY * source.Stride + sourceX * 4, 4)
                .CopyTo(pixels.AsSpan((y * width + x) * 4, 4));
        }
        return new CapturedFrame(width, height, width * 4, pixels, source.CapturedAtMonoMs, source.Sequence);
    }

    private static CapturedFrame Shift(CapturedFrame source, int dx, int dy)
    {
        byte[] pixels = new byte[source.BgraPixels.Length];
        for (int index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            int targetX = x + dx;
            int targetY = y + dy;
            if (targetX < 0 || targetX >= source.Width || targetY < 0 || targetY >= source.Height) continue;
            source.BgraPixels.Span.Slice(y * source.Stride + x * 4, 4)
                .CopyTo(pixels.AsSpan((targetY * source.Width + targetX) * 4, 4));
        }
        return source with { BgraPixels = pixels };
    }

    private static void Set(byte[] pixels, int width, int x, int y, byte b, byte g, byte r)
    {
        int offset = (y * width + x) * 4;
        pixels[offset] = b;
        pixels[offset + 1] = g;
        pixels[offset + 2] = r;
    }
}
