using System.Text.Json;

namespace Maple.Host.Stationary;

public sealed class VisualStationaryProfileStore(string directory)
{
    private readonly string profilePath = Path.Combine(directory, "profile.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<VisualProfileSaveResult> SaveAsync(
        VisualStationaryProfile profile,
        CancellationToken cancellationToken)
    {
        VisualProfileValidationResult validation = VisualStationaryProfileValidator.Validate(
            profile,
            profile.FrameWidth,
            profile.FrameHeight);
        if (!validation.IsValid) return new VisualProfileSaveResult(false, validation.Code);

        Directory.CreateDirectory(directory);
        string temporaryPath = profilePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, profilePath, overwrite: true);
            return new VisualProfileSaveResult(true, "VISUAL_PROFILE_SAVED");
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public async Task<VisualProfileLoadResult> LoadAsync(
        int currentFrameWidth,
        int currentFrameHeight,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(profilePath)) return new VisualProfileLoadResult(null, "VISUAL_PROFILE_NOT_CONFIGURED");
        try
        {
            await using FileStream stream = File.OpenRead(profilePath);
            VisualStationaryProfile? profile = await JsonSerializer.DeserializeAsync<VisualStationaryProfile>(
                stream,
                JsonOptions,
                cancellationToken);
            if (profile is null) return new VisualProfileLoadResult(null, "VISUAL_PROFILE_INVALID");
            VisualProfileValidationResult validation = VisualStationaryProfileValidator.Validate(
                profile,
                currentFrameWidth,
                currentFrameHeight);
            return validation.IsValid
                ? new VisualProfileLoadResult(profile, validation.Code)
                : new VisualProfileLoadResult(null, validation.Code);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new VisualProfileLoadResult(null, "VISUAL_PROFILE_INVALID");
        }
    }

    public Task<VisualProfileDeleteResult> DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (File.Exists(profilePath)) File.Delete(profilePath);
            return Task.FromResult(new VisualProfileDeleteResult(true, "VISUAL_PROFILE_CLEARED"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new VisualProfileDeleteResult(
                false,
                "VISUAL_PROFILE_CLEAR_FAILED:" + exception.GetType().Name));
        }
    }
}
