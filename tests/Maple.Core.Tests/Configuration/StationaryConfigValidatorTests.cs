using Maple.Core.Configuration;

namespace Maple.Core.Tests.Configuration;

public sealed class StationaryConfigValidatorTests
{
    [Fact]
    public void Default_configuration_is_valid()
    {
        ConfigValidationResult result = StationaryConfigValidator.Validate(StationaryAttackConfig.Default);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Rejects_attack_duration_above_product_limit()
    {
        StationaryAttackConfig config = StationaryAttackConfig.Default with
        {
            AttackBands = [new AttackBand(1, 60_001, 100)]
        };

        ConfigValidationResult result = StationaryConfigValidator.Validate(config);

        Assert.Contains(result.Errors, error => error.Code == "ATTACK_DURATION_LIMIT");
    }

    [Fact]
    public void Rejects_weights_that_do_not_total_one_hundred()
    {
        StationaryAttackConfig config = StationaryAttackConfig.Default with
        {
            AttackBands = [new AttackBand(1_000, 2_000, 99)]
        };

        ConfigValidationResult result = StationaryConfigValidator.Validate(config);

        Assert.Contains(result.Errors, error => error.Code == "ATTACK_WEIGHT_TOTAL");
    }

    [Theory]
    [InlineData("Alt")]
    [InlineData("F12")]
    [InlineData("")]
    public void Rejects_attack_keys_outside_the_shared_allowlist(string key)
    {
        StationaryAttackConfig config = StationaryAttackConfig.Default with { AttackKey = key };

        ConfigValidationResult result = StationaryConfigValidator.Validate(config);

        Assert.Contains(result.Errors, error => error.Code == "ATTACK_KEY_NOT_ALLOWED");
    }

    [Theory]
    [InlineData("MapleStory.exe")]
    [InlineData("/Applications/MapleStory.exe")]
    [InlineData("C:\\Games\\MapleStory.txt")]
    public void Rejects_non_absolute_windows_executable_paths(string path)
    {
        StationaryAttackConfig config = StationaryAttackConfig.Default with { TargetExecutablePath = path };

        ConfigValidationResult result = StationaryConfigValidator.Validate(config);

        Assert.Contains(result.Errors, error => error.Code == "TARGET_EXECUTABLE_PATH_INVALID");
    }

    [Fact]
    public void Rejects_disabled_monster_trigger_mode()
    {
        StationaryAttackConfig config = StationaryAttackConfig.Default with
        {
            AttackTriggerMode = AttackTriggerMode.MonsterInRange
        };

        ConfigValidationResult result = StationaryConfigValidator.Validate(config);

        Assert.Contains(result.Errors, error => error.Code == "ATTACK_TRIGGER_DISABLED");
    }
}
