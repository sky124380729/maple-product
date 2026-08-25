using Maple.Host.Navigation;
using Maple.Host.Preview;
using Maple.Host.Recognition;

namespace Maple.Host.Tests.Navigation;

public sealed class MapNameMatcherTests
{
    [Fact]
    public void Accepts_single_ocr_character_error_when_numeric_identity_matches()
    {
        MapNameMatch result = MapNameMatcher.Match("沼泽地3", "沼 泽 坦 3");

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void Rejects_similar_map_when_numeric_identity_differs()
    {
        MapNameMatch result = MapNameMatcher.Match("沼泽地3", "沼 泽 坦 2");

        Assert.False(result.IsMatch);
        Assert.Equal("MAP_NAME_MISMATCH", result.FaultCode);
    }

    [Fact]
    public void Requires_two_consistent_readings_before_verification()
    {
        MapNameVerificationGate gate = new("沼泽地3");

        Assert.Equal(MapNameVerification.Pending, gate.Update("沼 泽 坦 3"));
        Assert.Equal(MapNameVerification.Verified, gate.Update("沼 泽 坦 3"));
    }

    [Fact]
    public void Requires_two_consistent_mismatches_before_rejection()
    {
        MapNameVerificationGate gate = new("沼泽地3");

        Assert.Equal(MapNameVerification.Pending, gate.Update("沼 泽 坦 2"));
        Assert.Equal(MapNameVerification.Rejected, gate.Update("沼 泽 坦 2"));
    }

    [Fact]
    public void Projects_fixed_map_title_region_to_high_dpi_frame()
    {
        CapturedFrame frame = new(2049, 1152, 8196, new byte[2049 * 1152 * 4], 0, 1);

        bool resolved = MapNameOcrRegion.TryResolve(frame, out PixelRegion region);

        Assert.True(resolved);
        Assert.Equal(new PixelRegion(60, 45, 141, 56), region);
    }
}
