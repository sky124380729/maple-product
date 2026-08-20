using Maple.Host.Recognition;

namespace Maple.Host.Tests.Recognition;

public sealed class YoloTensorDecoderTests
{
    [Fact]
    public void Decodes_channels_first_and_suppresses_overlapping_class_boxes()
    {
        float[] tensor =
        [
            160, 164, 40,
            160, 164, 40,
            80, 80, 20,
            80, 80, 20,
            0.05f, 0.05f, 0.92f,
            0.95f, 0.90f, 0.04f,
        ];

        IReadOnlyList<YoloDetection> result = YoloTensorDecoder.DecodeChannelsFirst(
            tensor, ["character", "mob"], 3, 0.6, 0.45, 320, 320);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.ClassName == "character");
        Assert.Contains(result, item => item.ClassName == "mob");
    }
}
