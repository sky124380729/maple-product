using Maple.Host.Recognition;

namespace Maple.Host.Tests.Recognition;

public sealed class RecognitionTargetFilterTests
{
    [Fact]
    public void Rejects_nameplate_shaped_monster_box()
    {
        Assert.False(RecognitionTargetFilter.IsPlausibleMonster(
            new RecognitionTarget(330, 620, 50, 19, "monster", 0.7)));
    }

    [Fact]
    public void Rejects_drop_that_is_oversized_or_overlaps_self()
    {
        var self = new SelfObservation(400, 300, 55, 60, null, 0.9);

        Assert.False(RecognitionTargetFilter.IsPlausibleDrop(
            new RecognitionTarget(390, 300, 50, 50, "drop", 0.8), self));
        Assert.False(RecognitionTargetFilter.IsPlausibleDrop(
            new RecognitionTarget(400, 350, 120, 20, "drop", 0.8)));
    }

    [Fact]
    public void Accepts_plausible_monster_and_drop_boxes()
    {
        Assert.True(RecognitionTargetFilter.IsPlausibleMonster(
            new RecognitionTarget(800, 500, 52, 72, "monster", 0.8)));
        Assert.True(RecognitionTargetFilter.IsPlausibleDrop(
            new RecognitionTarget(820, 570, 22, 16, "drop", 0.8)));
    }

    [Fact]
    public void Requires_two_nearby_observations_before_publishing_drop()
    {
        var stabilizer = new RecognitionTargetStabilizer();
        var first = new RecognitionTarget(820, 570, 22, 16, "drop", 0.8);
        var second = first with { X = 824, Y = 571, Confidence = 0.85 };

        Assert.Empty(stabilizer.Update([first], 10));
        IReadOnlyList<RecognitionTarget> stable = stabilizer.Update([second], 11);

        Assert.Single(stable);
        Assert.Equal(0.85, stable[0].Confidence);
    }

    [Fact]
    public void Removes_stale_track_after_gap()
    {
        var stabilizer = new RecognitionTargetStabilizer();
        var drop = new RecognitionTarget(820, 570, 22, 16, "drop", 0.8);

        stabilizer.Update([drop], 10);
        Assert.Single(stabilizer.Update([drop], 11));
        Assert.Empty(stabilizer.Update([], 14));
    }
}
