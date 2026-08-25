using System.Text.Json;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class VisualStationaryProfileStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "maple-visual-profile-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Accepts_a_textured_profile_for_the_same_viewport()
    {
        VisualProfileValidationResult result = VisualStationaryProfileValidator.Validate(ValidProfile(), 1366, 768);

        Assert.True(result.IsValid);
        Assert.Equal("VISUAL_PROFILE_READY", result.Code);
    }

    [Fact]
    public void Rejects_a_profile_for_a_different_viewport()
    {
        VisualProfileValidationResult result = VisualStationaryProfileValidator.Validate(ValidProfile(), 1600, 900);

        Assert.False(result.IsValid);
        Assert.Equal("VISUAL_VIEWPORT_MISMATCH", result.Code);
    }

    [Theory]
    [InlineData(1300, 300, 100, 80, "VISUAL_PLATFORM_OUT_OF_FRAME")]
    [InlineData(100, 300, 60, 80, "VISUAL_PLATFORM_TOO_NARROW")]
    public void Rejects_invalid_platform_rectangles(int x, int y, int width, int height, string code)
    {
        VisualStationaryProfile profile = ValidProfile() with { Platform = new FrameRect(x, y, width, height) };

        VisualProfileValidationResult result = VisualStationaryProfileValidator.Validate(profile, 1366, 768);

        Assert.False(result.IsValid);
        Assert.Equal(code, result.Code);
    }

    [Fact]
    public void Rejects_a_uniform_name_template()
    {
        VisualStationaryProfile profile = ValidProfile() with { NameTemplateBgra = new byte[20 * 8 * 4] };

        VisualProfileValidationResult result = VisualStationaryProfileValidator.Validate(profile, 1366, 768);

        Assert.False(result.IsValid);
        Assert.Equal("VISUAL_NAME_TEMPLATE_LOW_TEXTURE", result.Code);
    }

    [Fact]
    public void Rejects_a_name_template_tall_enough_to_include_multiple_nameplate_rows()
    {
        const int width = 80, height = 25;
        byte[] pixels = Enumerable.Range(0, width * height * 4)
            .Select(index => (byte)(index % 251))
            .ToArray();
        VisualStationaryProfile profile = ValidProfile() with
        {
            NameSource = new FrameRect(420, 260, width, height),
            NameTemplateWidth = width,
            NameTemplateHeight = height,
            NameTemplateBgra = pixels
        };

        VisualProfileValidationResult result = VisualStationaryProfileValidator.Validate(profile, 1366, 768);

        Assert.False(result.IsValid);
        Assert.Equal("VISUAL_NAME_TEMPLATE_TOO_TALL", result.Code);
    }

    [Fact]
    public async Task Saves_and_loads_the_complete_profile_atomically()
    {
        VisualStationaryProfile expected = ValidProfile();
        var store = new VisualStationaryProfileStore(root);

        VisualProfileSaveResult saved = await store.SaveAsync(expected, CancellationToken.None);
        VisualProfileLoadResult loaded = await store.LoadAsync(1366, 768, CancellationToken.None);

        Assert.True(saved.Success);
        Assert.Equal("VISUAL_PROFILE_SAVED", saved.Code);
        Assert.Equal("VISUAL_PROFILE_READY", loaded.Code);
        Assert.NotNull(loaded.Profile);
        Assert.Equal(expected with { NameTemplateBgra = [] }, loaded.Profile with { NameTemplateBgra = [] });
        Assert.Equal(expected.NameTemplateBgra, loaded.Profile.NameTemplateBgra);
    }

    [Fact]
    public async Task Loads_a_schema_one_name_profile_without_character_properties()
    {
        Directory.CreateDirectory(root);
        VisualStationaryProfile legacy = ValidProfile() with { SchemaVersion = 1 };
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = legacy.SchemaVersion,
            frameWidth = legacy.FrameWidth,
            frameHeight = legacy.FrameHeight,
            platform = legacy.Platform,
            nameSource = legacy.NameSource,
            nameTemplateWidth = legacy.NameTemplateWidth,
            nameTemplateHeight = legacy.NameTemplateHeight,
            nameTemplateBgra = legacy.NameTemplateBgra,
            updatedAtUtc = legacy.UpdatedAtUtc
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(Path.Combine(root, "profile.json"), json);
        var store = new VisualStationaryProfileStore(root);

        VisualProfileLoadResult loaded = await store.LoadAsync(1366, 768, CancellationToken.None);

        Assert.NotNull(loaded.Profile);
        Assert.Equal(VisualIdentityKind.NameTemplate, loaded.Profile.IdentityKind);
        Assert.Null(loaded.Profile.CharacterAppearance);
    }

    [Fact]
    public async Task Saves_and_loads_a_schema_two_character_template_bank()
    {
        byte[][] templates = [TexturedPixels(48, 72, 0), TexturedPixels(48, 72, 17)];
        VisualStationaryProfile expected = CharacterProfile(48, 72, templates);
        var store = new VisualStationaryProfileStore(root);

        VisualProfileSaveResult saved = await store.SaveAsync(expected, CancellationToken.None);
        VisualProfileLoadResult loaded = await store.LoadAsync(1366, 768, CancellationToken.None);

        Assert.True(saved.Success);
        Assert.NotNull(loaded.Profile?.CharacterAppearance);
        Assert.Equal(VisualIdentityKind.CharacterAppearance, loaded.Profile.IdentityKind);
        Assert.Equal(2, loaded.Profile.CharacterAppearance.TemplatesBgra.Length);
        Assert.Equal(templates[0], loaded.Profile.CharacterAppearance.TemplatesBgra[0]);
        Assert.Equal(templates[1], loaded.Profile.CharacterAppearance.TemplatesBgra[1]);
    }

    [Theory]
    [InlineData(23, 32, "VISUAL_CHARACTER_TEMPLATE_TOO_SMALL")]
    [InlineData(24, 31, "VISUAL_CHARACTER_TEMPLATE_TOO_SMALL")]
    [InlineData(113, 144, "VISUAL_CHARACTER_TEMPLATE_TOO_LARGE")]
    [InlineData(112, 145, "VISUAL_CHARACTER_TEMPLATE_TOO_LARGE")]
    public void Rejects_character_patches_outside_scaled_size_limits(int width, int height, string code)
    {
        VisualStationaryProfile profile = CharacterProfile(width, height, [TexturedPixels(width, height, 0)]);

        VisualProfileValidationResult result = VisualStationaryProfileValidator.Validate(profile, 1366, 768);

        Assert.False(result.IsValid);
        Assert.Equal(code, result.Code);
    }

    [Fact]
    public void Accepts_a_textured_character_patch_without_the_name_height_limit()
    {
        VisualStationaryProfile profile = CharacterProfile(48, 72, [TexturedPixels(48, 72, 0)]);

        VisualProfileValidationResult result = VisualStationaryProfileValidator.Validate(profile, 1366, 768);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rejects_character_template_with_wrong_pixel_length()
    {
        VisualStationaryProfile profile = CharacterProfile(48, 72, [new byte[48 * 72 * 4 - 1]]);

        VisualProfileValidationResult result = VisualStationaryProfileValidator.Validate(profile, 1366, 768);

        Assert.False(result.IsValid);
        Assert.Equal("VISUAL_CHARACTER_TEMPLATE_INVALID", result.Code);
    }

    [Fact]
    public void Rejects_uniform_character_template()
    {
        VisualStationaryProfile profile = CharacterProfile(48, 72, [new byte[48 * 72 * 4]]);

        VisualProfileValidationResult result = VisualStationaryProfileValidator.Validate(profile, 1366, 768);

        Assert.False(result.IsValid);
        Assert.Equal("VISUAL_CHARACTER_TEMPLATE_LOW_TEXTURE", result.Code);
    }

    [Fact]
    public void Rejects_an_unknown_schema_two_identity_kind()
    {
        VisualStationaryProfile profile = ValidProfile() with { IdentityKind = (VisualIdentityKind)999 };

        VisualProfileValidationResult result = VisualStationaryProfileValidator.Validate(profile, 1366, 768);

        Assert.False(result.IsValid);
        Assert.Equal("VISUAL_IDENTITY_KIND_UNSUPPORTED", result.Code);
    }

    [Fact]
    public async Task A_cancelled_save_does_not_replace_the_existing_profile()
    {
        VisualStationaryProfile original = ValidProfile();
        var store = new VisualStationaryProfileStore(root);
        Assert.True((await store.SaveAsync(original, CancellationToken.None)).Success);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveAsync(original with { UpdatedAtUtc = original.UpdatedAtUtc.AddMinutes(1) }, cancellation.Token));
        VisualProfileLoadResult loaded = await store.LoadAsync(1366, 768, CancellationToken.None);

        Assert.Equal(original.UpdatedAtUtc, loaded.Profile!.UpdatedAtUtc);
    }

    [Fact]
    public async Task Clears_the_saved_visual_profile_idempotently()
    {
        var store = new VisualStationaryProfileStore(root);
        Assert.True((await store.SaveAsync(ValidProfile(), CancellationToken.None)).Success);

        VisualProfileDeleteResult first = await store.DeleteAsync(CancellationToken.None);
        VisualProfileDeleteResult second = await store.DeleteAsync(CancellationToken.None);
        VisualProfileLoadResult loaded = await store.LoadAsync(1366, 768, CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal("VISUAL_PROFILE_CLEARED", first.Code);
        Assert.Equal("VISUAL_PROFILE_NOT_CONFIGURED", loaded.Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static VisualStationaryProfile ValidProfile()
    {
        const int templateWidth = 20, templateHeight = 8;
        byte[] pixels = new byte[templateWidth * templateHeight * 4];
        for (int index = 0; index < templateWidth * templateHeight; index++)
        {
            pixels[index * 4] = (byte)(20 + index % 120);
            pixels[index * 4 + 1] = (byte)(210 - index % 100);
            pixels[index * 4 + 2] = (byte)(40 + index % 180);
            pixels[index * 4 + 3] = 255;
        }
        return new VisualStationaryProfile(
            VisualStationaryProfile.SchemaVersionCurrent,
            1366,
            768,
            new FrameRect(100, 300, 800, 80),
            new FrameRect(420, 260, templateWidth, templateHeight),
            templateWidth,
            templateHeight,
            pixels,
            DateTimeOffset.Parse("2026-08-22T00:00:00Z"));
    }

    private static VisualStationaryProfile CharacterProfile(int width, int height, byte[][] templates) =>
        ValidProfile() with
        {
            SchemaVersion = VisualStationaryProfile.SchemaVersionCurrent,
            IdentityKind = VisualIdentityKind.CharacterAppearance,
            CharacterAppearance = new VisualCharacterTemplateBank(
                new FrameRect(420, 220, width, height),
                width,
                height,
                templates,
                1)
        };

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
