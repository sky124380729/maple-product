using Maple.Host.Recognition;

namespace Maple.Host.Tests.Recognition;

public sealed class RecognitionOverlayLayoutTests
{
    [Fact]
    public void Scales_and_centers_character_and_monster_boxes_without_drops()
    {
        var snapshot = RecognitionSnapshot.Create(
            "s", null, 1, 10, 20, HudObservation.Empty,
            [new RecognitionTarget(400, 300, 100, 80, "monster", 0.9)],
            [new RecognitionTarget(100, 100, 20, 20, "drop", 0.8)],
            [new RecognitionTarget(600, 200, 50, 100, "player", 0.85)],
            new SelfObservation(200, 200, 50, 100, null, 0.95));

        IReadOnlyList<RecognitionOverlayBox> boxes = RecognitionOverlayLayout.Create(snapshot, 800, 600, 1000, 600);

        Assert.Equal(2, boxes.Count);
        Assert.Contains(boxes, box => box.Kind == "self" && box.X == 300 && box.Y == 200);
        Assert.Contains(boxes, box => box.Kind == "monster" && box.X == 500 && box.Y == 300);
        Assert.DoesNotContain(boxes, box => box.Kind == "drop");
        Assert.DoesNotContain(boxes, box => box.Kind == "player");
    }
}
