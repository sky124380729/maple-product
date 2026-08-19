using Maple.Core.Configuration;
using Maple.Host.Configuration;

namespace Maple.Host.Tests.Configuration;

public sealed class JsonConfigStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "maple-product-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Saves_and_loads_the_last_valid_configuration()
    {
        var store = new JsonConfigStore(Path.Combine(directory, "stationary.json"));
        StationaryAttackConfig config = StationaryAttackConfig.Default with { AttackKey = "Space" };

        ConfigStoreResult saved = await store.SaveAsync(config, CancellationToken.None);
        ConfigLoadResult loaded = await store.LoadAsync(CancellationToken.None);

        Assert.True(saved.Success);
        Assert.Equal("Space", loaded.Config.AttackKey);
        Assert.Null(loaded.WarningCode);
    }

    [Fact]
    public async Task Rejects_invalid_configuration_without_overwriting_the_last_valid_file()
    {
        var store = new JsonConfigStore(Path.Combine(directory, "stationary.json"));
        await store.SaveAsync(StationaryAttackConfig.Default, CancellationToken.None);

        ConfigStoreResult rejected = await store.SaveAsync(
            StationaryAttackConfig.Default with { AttackKey = "Alt" },
            CancellationToken.None);
        ConfigLoadResult loaded = await store.LoadAsync(CancellationToken.None);

        Assert.False(rejected.Success);
        Assert.Equal("Ctrl", loaded.Config.AttackKey);
    }

    [Fact]
    public async Task Falls_back_to_safe_defaults_when_the_file_is_corrupt()
    {
        string path = Path.Combine(directory, "stationary.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "{broken");
        var store = new JsonConfigStore(path);

        ConfigLoadResult loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(StationaryAttackConfig.Default, loaded.Config);
        Assert.Equal("CONFIG_FILE_CORRUPT", loaded.WarningCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
