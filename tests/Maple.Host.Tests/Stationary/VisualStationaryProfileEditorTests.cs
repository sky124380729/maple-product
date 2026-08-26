using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class VisualStationaryProfileEditorTests
{
    [Fact]
    public void Replaces_platform_without_replacing_the_character_template_bank()
    {
        DateTimeOffset capturedAt = DateTimeOffset.Parse("2026-08-26T00:30:00Z");
        var bank = new VisualCharacterTemplateBank(
            new FrameRect(420, 220, 48, 72),
            48,
            72,
            [TexturedPixels(48, 72, 0), TexturedPixels(48, 72, 17)],
            1,
            capturedAt);
        VisualStationaryProfile original = CharacterProfile(bank);
        FrameRect replacement = new(180, 260, 700, 120);
        DateTimeOffset updatedAt = DateTimeOffset.Parse("2026-08-26T01:00:00Z");

        VisualProfileEditResult result = VisualStationaryProfileEditor.ReplacePlatform(
            original,
            replacement,
            1366,
            768,
            updatedAt);

        Assert.True(result.Success);
        Assert.Equal("VISUAL_PLATFORM_SAVED", result.Code);
        Assert.NotNull(result.Profile);
        Assert.Equal(replacement, result.Profile.Platform);
        Assert.Equal(updatedAt, result.Profile.UpdatedAtUtc);
        Assert.Same(bank, result.Profile.CharacterAppearance);
        Assert.Equal(capturedAt, result.Profile.CharacterAppearance!.CapturedAtUtc);
        Assert.Equal(bank.TemplatesBgra[0], result.Profile.CharacterAppearance.TemplatesBgra[0]);
    }

    [Fact]
    public void Refuses_to_reuse_a_legacy_name_template_as_character_appearance()
    {
        VisualStationaryProfile legacy = NameProfile();

        VisualProfileEditResult result = VisualStationaryProfileEditor.ReplacePlatform(
            legacy,
            new FrameRect(180, 260, 700, 120),
            1366,
            768,
            DateTimeOffset.UtcNow);

        Assert.False(result.Success);
        Assert.Null(result.Profile);
        Assert.Equal("VISUAL_CHARACTER_TEMPLATE_NOT_CONFIGURED", result.Code);
    }

    [Fact]
    public void Refuses_to_reuse_character_pixels_for_a_different_viewport()
    {
        var bank = new VisualCharacterTemplateBank(
            new FrameRect(420, 220, 48, 72),
            48,
            72,
            [TexturedPixels(48, 72, 0)],
            1,
            DateTimeOffset.UtcNow);

        VisualProfileEditResult result = VisualStationaryProfileEditor.ReplacePlatform(
            CharacterProfile(bank),
            new FrameRect(180, 260, 700, 120),
            1600,
            900,
            DateTimeOffset.UtcNow);

        Assert.False(result.Success);
        Assert.Equal("VISUAL_VIEWPORT_MISMATCH", result.Code);
    }

    private static VisualStationaryProfile CharacterProfile(VisualCharacterTemplateBank bank) =>
        NameProfile() with
        {
            IdentityKind = VisualIdentityKind.CharacterAppearance,
            CharacterAppearance = bank
        };

    private static VisualStationaryProfile NameProfile()
    {
        const int width = 20, height = 8;
        return new VisualStationaryProfile(
            VisualStationaryProfile.SchemaVersionCurrent,
            1366,
            768,
            new FrameRect(100, 300, 800, 100),
            new FrameRect(420, 260, width, height),
            width,
            height,
            TexturedPixels(width, height, 0),
            DateTimeOffset.Parse("2026-08-22T00:00:00Z"));
    }

    private static byte[] TexturedPixels(int width, int height, int seed)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int index = 0; index < width * height; index++)
        {
            pixels[index * 4] = (byte)(20 + (index * 7 + seed) % 220);
            pixels[index * 4 + 1] = (byte)(230 - (index * 5 + seed) % 200);
            pixels[index * 4 + 2] = (byte)(40 + (index * 11 + seed) % 210);
            pixels[index * 4 + 3] = 255;
        }
        return pixels;
    }
}
