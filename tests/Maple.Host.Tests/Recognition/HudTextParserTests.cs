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

        HudFrameLayout physical = AdaptiveHudLayout.Resolve(2051, 1200);
        Assert.InRange(physical.Identity.Y, 1135, 1145);
        Assert.InRange(physical.HpText.Height, 55, 65);
    }

    [Fact]
    public void Rejects_resource_ocr_when_current_exceeds_maximum()
    {
        HudResource resource = HudTextParser.ParseResource("广告 586 / 158");

        Assert.Null(resource.Current);
        Assert.Null(resource.Maximum);
    }

    [Fact]
    public void Rejects_identity_text_when_level_marker_is_buried_in_chat_text()
    {
        HudIdentity identity = HudTextParser.ParseIdentity("逍遥大柜 出金 100R=300万金币 猎人 LV. 0");

        Assert.Null(identity.CharacterName);
        Assert.Null(identity.Job);
        Assert.Null(identity.Level);
    }

    [Theory]
    [InlineData("HP [ 1 S86/1 S86]", 1586, 1586)]
    [InlineData("HP [ 0 S86/1 S86]", 1586, 1586)]
    [InlineData("MP [ 3 引 / 3 引 ]", 991, 991)]
    [InlineData("HP [ 1 s86 ／ 1 s86 ]", 1586, 1586)]
    public void Repairs_common_ocr_spacing_and_character_substitutions(string text, int current, int maximum)
    {
        HudResource resource = HudTextParser.ParseResource(text);

        Assert.Equal(current, resource.Current);
        Assert.Equal(maximum, resource.Maximum);
    }

    [Fact]
    public void Parses_real_frame_identity_and_experience_ocr()
    {
        HudIdentity identity = HudTextParser.ParseIdentity("LV. 0 猎 人 Pink 、 Bin");

        Assert.Null(identity.Level);
        Assert.Equal("猎人", identity.Job);
        Assert.Equal("Pink丶Bin", identity.CharacterName);
        Assert.Equal(0.23, HudTextParser.ParseExperience("E)(P 30s0．23"));
    }

    [Fact]
    public void Keeps_level_and_job_when_name_row_is_unreadable()
    {
        HudIdentity identity = HudTextParser.ParseIdentity("LV. @9! 猎 人 LV. 4 3 猖 人");

        Assert.Equal(43, identity.Level);
        Assert.Equal("猎人", identity.Job);
        Assert.Null(identity.CharacterName);
    }

    [Fact]
    public void Reassembles_split_latin_character_name_from_real_frame_ocr()
    {
        Assert.Equal("Pink丶Bin", HudTextParser.ExtractLatinName("LV. ． 4 3 猎 人 Pi n k 、 Bin"));
        Assert.Equal("猎人", HudTextParser.ExtractJob("LV. ． 4 3 猖 人 Pi n k 、 Bin"));

        HudIdentity identity = HudTextParser.ParseIdentity("LV. ． 4 3 猖 人 Pi n k 、 Bin");

        Assert.Equal(43, identity.Level);
        Assert.Equal("猎人", identity.Job);
        Assert.Equal("Pink丶Bin", identity.CharacterName);
    }

    [Theory]
    [InlineData("E)(P 30S 囤．23 氵引", 0.23)]
    [InlineData("EXP 90% (0.23%)", 0.23)]
    [InlineData("EXP .23", 0.23)]
    public void Parses_experience_when_ocr_splits_fraction_or_percent(string text, double expected)
    {
        Assert.Equal(expected, HudTextParser.ParseExperience(text));
    }
}
