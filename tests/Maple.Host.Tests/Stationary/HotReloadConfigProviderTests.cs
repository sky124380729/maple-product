using Maple.Core.Configuration;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class HotReloadConfigProviderTests
{
    [Fact]
    public void Starts_with_an_immutable_validated_snapshot()
    {
        var provider = new HotReloadConfigProvider(StationaryAttackConfig.Default);

        StationaryAttackConfig snapshot = provider.GetValidatedSnapshot();
        Assert.Equal(StationaryAttackConfig.Default.AttackKey, snapshot.AttackKey);
        Assert.Equal(StationaryAttackConfig.Default.AttackBands, snapshot.AttackBands);
    }

    [Fact]
    public void Rejects_invalid_updates_and_keeps_the_previous_snapshot()
    {
        var provider = new HotReloadConfigProvider(StationaryAttackConfig.Default);

        ConfigProviderUpdateResult result = provider.TryUpdate(StationaryAttackConfig.Default with { AttackKey = "Alt" });

        Assert.False(result.Success);
        Assert.Equal("CONFIG_INVALID", result.Code);
        Assert.Equal("Ctrl", provider.GetValidatedSnapshot().AttackKey);
    }

    [Fact]
    public void Publishes_a_valid_update_as_one_complete_snapshot()
    {
        var provider = new HotReloadConfigProvider(StationaryAttackConfig.Default);
        StationaryAttackConfig updated = StationaryAttackConfig.Default with
        {
            AttackKey = "Space",
            MaxLateralMoveMs = 140,
            MoveHoldMinMs = 90,
            MoveHoldMaxMs = 140
        };

        ConfigProviderUpdateResult result = provider.TryUpdate(updated);

        Assert.True(result.Success);
        StationaryAttackConfig snapshot = provider.GetValidatedSnapshot();
        Assert.Equal(updated.AttackKey, snapshot.AttackKey);
        Assert.Equal(updated.MoveHoldMinMs, snapshot.MoveHoldMinMs);
        Assert.Equal(updated.MoveHoldMaxMs, snapshot.MoveHoldMaxMs);
        Assert.Equal(updated.AttackBands, snapshot.AttackBands);
    }

    [Fact]
    public void Returned_attack_bands_cannot_mutate_the_published_snapshot()
    {
        var provider = new HotReloadConfigProvider(StationaryAttackConfig.Default);
        StationaryAttackConfig firstRead = provider.GetValidatedSnapshot();

        Assert.IsType<AttackBand[]>(firstRead.AttackBands)[0] = new AttackBand(1, 1, 100);

        Assert.Equal(1_000, provider.GetValidatedSnapshot().AttackBands[0].MinMs);
    }
}
