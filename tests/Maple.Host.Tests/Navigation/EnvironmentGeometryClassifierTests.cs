using Maple.Host.Navigation;
using Maple.Host.Recognition;

namespace Maple.Host.Tests.Navigation;

public sealed class EnvironmentGeometryClassifierTests
{
    [Fact]
    public void Classifies_wide_thin_environment_boxes_as_platforms()
    {
        RecognitionTarget detection = new(200, 400, 600, 18, "environment", 0.8);

        MapFrameGeometry geometry = EnvironmentGeometryClassifier.Classify([detection], 1000, 800);

        MapPlatformCandidate platform = Assert.Single(geometry.Platforms);
        Assert.Equal(0.2, platform.XMin, 3);
        Assert.Equal(0.8, platform.XMax, 3);
        Assert.Equal(0.511, platform.Y, 3);
    }

    [Fact]
    public void Classifies_narrow_tall_environment_boxes_as_ladders()
    {
        RecognitionTarget detection = new(490, 160, 20, 320, "environment", 0.75);

        MapFrameGeometry geometry = EnvironmentGeometryClassifier.Classify([detection], 1000, 800);

        MapLadderCandidate ladder = Assert.Single(geometry.Ladders);
        Assert.Equal(0.5, ladder.X, 3);
        Assert.Equal(0.2, ladder.YMin, 3);
        Assert.Equal(0.6, ladder.YMax, 3);
    }

    [Fact]
    public void Rejects_square_low_confidence_and_hud_environment_boxes()
    {
        RecognitionTarget[] detections =
        [
            new(400, 300, 100, 90, "environment", 0.9),
            new(100, 400, 600, 18, "environment", 0.2),
            new(10, 10, 90, 12, "environment", 0.9),
            new(200, 760, 600, 18, "environment", 0.9)
        ];

        MapFrameGeometry geometry = EnvironmentGeometryClassifier.Classify(detections, 1000, 800);

        Assert.Empty(geometry.Platforms);
        Assert.Empty(geometry.Ladders);
    }
}
