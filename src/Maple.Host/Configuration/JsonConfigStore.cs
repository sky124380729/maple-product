using System.Text.Json;
using System.Text.Json.Serialization;
using Maple.Core.Configuration;

namespace Maple.Host.Configuration;

public sealed record ConfigStoreResult(bool Success, string Code)
{
    public static ConfigStoreResult Saved() => new(true, "CONFIG_SAVED");
    public static ConfigStoreResult Rejected(string code) => new(false, code);
}

public sealed record ConfigLoadResult(StationaryAttackConfig Config, string? WarningCode);

public sealed class JsonConfigStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public async Task<ConfigStoreResult> SaveAsync(
        StationaryAttackConfig config,
        CancellationToken cancellationToken)
    {
        ConfigValidationResult validation = StationaryConfigValidator.Validate(config);
        if (!validation.IsValid) return ConfigStoreResult.Rejected("CONFIG_INVALID");

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (FileStream stream = new(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
        return ConfigStoreResult.Saved();
    }

    public async Task<ConfigLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new ConfigLoadResult(StationaryAttackConfig.Default, null);
        try
        {
            await using FileStream stream = File.OpenRead(path);
            StationaryAttackConfig? config = await JsonSerializer.DeserializeAsync<StationaryAttackConfig>(
                stream,
                JsonOptions,
                cancellationToken);
            if (config is null || !StationaryConfigValidator.Validate(config).IsValid)
                return new ConfigLoadResult(StationaryAttackConfig.Default, "CONFIG_FILE_INVALID");
            return new ConfigLoadResult(config, null);
        }
        catch (JsonException)
        {
            return new ConfigLoadResult(StationaryAttackConfig.Default, "CONFIG_FILE_CORRUPT");
        }
    }
}
