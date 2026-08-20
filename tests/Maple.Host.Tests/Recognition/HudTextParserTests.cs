using Maple.Host.Recognition;

namespace Maple.Host.Tests.Recognition;

public sealed class HudTextParserTests
{
    [Fact]
    public void Parses_identity_resources_and_experience()
    {
        HudIdentity identity = HudTextParser.ParseIdentity("LV. 43  猎人  Pink丶Bin");
        HudResource hp = HudTextParser.ParseResource("1586 / 1586");

        Assert.Equal(43, identity.Level);
        Assert.Equal("猎人", identity.Job);
        Assert.Equal("Pink丶Bin", identity.CharacterName);
        Assert.Equal(1586, hp.Current);
        Assert.Equal(1586, hp.Maximum);
        Assert.Equal(0.23, HudTextParser.ParseExperience("EXP 90% (0.23%)"));
    }

    [Fact]
    public void Resolves_hud_regions_relative_to_frame_size()
    {
        HudFrameLayout layout = AdaptiveHudLayout.Resolve(1366, 768);

        Assert.InRange(layout.Identity.X, 275, 285);
        Assert.InRange(layout.Identity.Y, 730, 740);
        Assert.True(layout.HpText.X < layout.MpText.X);
        Assert.True(layout.MpText.X < layout.ExpText.X);
    }
}
