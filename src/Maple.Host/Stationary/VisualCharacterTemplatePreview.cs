namespace Maple.Host.Stationary;

public sealed record VisualCharacterTemplatePreviewItem(
    int Width,
    int Height,
    ReadOnlyMemory<byte> BgraPixels);

public sealed record VisualCharacterTemplatePreviewModel(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<VisualCharacterTemplatePreviewItem> Items);

public static class VisualCharacterTemplatePreview
{
    public static VisualCharacterTemplatePreviewModel? Create(VisualStationaryProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        VisualCharacterTemplateBank? bank = profile.CharacterAppearance;
        if (profile.IdentityKind != VisualIdentityKind.CharacterAppearance ||
            bank is null ||
            bank.TemplatesBgra is not { Length: > 0 })
            return null;

        VisualCharacterTemplatePreviewItem[] items = bank.TemplatesBgra
            .Select(pixels => new VisualCharacterTemplatePreviewItem(
                bank.TemplateWidth,
                bank.TemplateHeight,
                pixels))
            .ToArray();
        return new VisualCharacterTemplatePreviewModel(
            bank.CapturedAtUtc ?? profile.UpdatedAtUtc,
            items);
    }
}
