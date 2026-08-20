using Maple.Host.Recognition;

namespace Maple.Host.Tests.Recognition;

public sealed class RecognitionContractsTests
{
    [Fact]
    public void Snapshot_copies_collections_and_preserves_frame_age()
    {
        var monsters = new List<RecognitionTarget> { new(10, 20, 30, 40, "monster", 0.9) };
        var snapshot = RecognitionSnapshot.Create(
            "s1", null, 1, 1000, 1100, HudObservation.Empty, monsters, [], [], null);

        monsters.Clear();

        Assert.Single(snapshot.Monsters);
        Assert.Equal(RecognitionHealth.Running, snapshot.Health);
        Assert.Equal(100, snapshot.FrameAgeMs);
    }

    [Fact]
    public void Snapshot_marks_stale_when_frame_age_exceeds_threshold()
    {
        var snapshot = RecognitionSnapshot.Create(
            "s1", null, 1, 1000, 1301, HudObservation.Empty, [], [], [], null, staleAfterMs: 300);

        Assert.Equal(RecognitionHealth.Stale, snapshot.Health);
    }
}
