using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class VisualCharacterTemplatePreviewTests
{
    [Fact]
    public void Creates_one_real_preview_item_per_saved_character_template()
    {
        DateTimeOffset capturedAt = DateTimeOffset.Parse("2026-08-26T01:30:00Z");
        byte[][] templates = [TexturedPixels(48, 72, 0), TexturedPixels(48, 72, 23)];
        VisualStationaryProfile profile = CharacterProfile(templates, capturedAt);

        VisualCharacterTemplatePreviewModel? preview = VisualCharacterTemplatePreview.Create(profile);

        Assert.NotNull(preview);
        Assert.Equal(capturedAt, preview.CapturedAtUtc);
        Assert.Equal(2, preview.Items.Count);
        Assert.All(preview.Items, item =>
        {
            Assert.Equal(48, item.Width);
            Assert.Equal(72, item.Height);
        });
        Assert.Equal(templates[0], preview.Items[0].BgraPixels.ToArray());
        Assert.Equal(templates[1], preview.Items[1].BgraPixels.ToArray());
    }

    [Fact]
    public void Uses_profile_update_time_for_an_older_bank_without_capture_metadata()
    {
        VisualStationaryProfile profile = CharacterProfile(
            [TexturedPixels(48, 72, 0)],
            capturedAtUtc: null);

        VisualCharacterTemplatePreviewModel? preview = VisualCharacterTemplatePreview.Create(profile);

        Assert.NotNull(preview);
        Assert.Equal(profile.UpdatedAtUtc, preview.CapturedAtUtc);
    }

    [Fact]
    public void Does_not_create_an_empty_preview_for_a_name_profile()
    {
        VisualStationaryProfile profile = CharacterProfile(
            [TexturedPixels(48, 72, 0)],
            DateTimeOffset.UtcNow) with
        {
            IdentityKind = VisualIdentityKind.NameTemplate,
            CharacterAppearance = null
        };

        Assert.Null(VisualCharacterTemplatePreview.Create(profile));
    }

    private static VisualStationaryProfile CharacterProfile(
        byte[][] templates,
        DateTimeOffset? capturedAtUtc) => new(
        VisualStationaryProfile.SchemaVersionCurrent,
        1366,
        768,
        new FrameRect(100, 300, 800, 100),
        new FrameRect(0, 0, 0, 0),
        0,
        0,
        [],
        DateTimeOffset.Parse("2026-08-26T00:00:00Z"),
        VisualIdentityKind.CharacterAppearance,
        new VisualCharacterTemplateBank(
            new FrameRect(420, 220, 48, 72),
            48,
            72,
            templates,
            1,
            capturedAtUtc));

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
