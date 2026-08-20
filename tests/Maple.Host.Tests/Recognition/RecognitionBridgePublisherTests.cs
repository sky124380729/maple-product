using Maple.Host.Recognition;

namespace Maple.Host.Tests.Recognition;

public sealed class RecognitionBridgePublisherTests
{
    [Fact]
    public void Publishes_at_most_four_updates_per_second_and_maps_hud()
    {
        var sent = new List<RecognitionBridgeMessage>();
        var publisher = new RecognitionBridgePublisher(sent.Add, minimumIntervalMs: 250);
        var hud = new HudObservation("Pink丶Bin", 43, "猎人", 1586, 1586, 914, 991, 1, 0.922, 0.23, 0.88);

        publisher.TryPublish(RecognitionSnapshot.Create("s", null, 1, 1000, 1010, hud, [], [], [], null), 1010);
        publisher.TryPublish(RecognitionSnapshot.Create("s", null, 2, 1020, 1100, hud, [], [], [], null), 1100);
        publisher.TryPublish(RecognitionSnapshot.Create("s", null, 3, 1200, 1260, hud, [], [], [], null), 1260);

        Assert.Equal(2, sent.Count);
        Assert.Equal("recognition.snapshot", sent[0].Type);
        Assert.Equal("Pink丶Bin", sent[0].Snapshot.Hud.CharacterName);
        Assert.Equal(43, sent[0].Snapshot.Hud.Level);
        Assert.Equal(10, sent[0].Snapshot.FrameAgeMs);
    }
}
