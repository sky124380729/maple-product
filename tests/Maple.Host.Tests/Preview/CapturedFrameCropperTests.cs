using Maple.Host.Preview;

namespace Maple.Host.Tests.Preview;

public sealed class CapturedFrameCropperTests
{
    [Fact]
    public void Crops_client_pixels_and_rewrites_stride()
    {
        byte[] pixels = new byte[5 * 4 * 4];
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 5; column++)
                pixels[(row * 5 + column) * 4] = (byte)(row * 10 + column);
        CapturedFrame frame = new(5, 4, 20, pixels, 1, 2);

        CapturedFrame cropped = CapturedFrameCropper.Crop(frame, 1, 1, 3, 2);

        Assert.Equal(3, cropped.Width);
        Assert.Equal(2, cropped.Height);
        Assert.Equal(12, cropped.Stride);
        Assert.Equal(new byte[] { 11, 12, 13, 21, 22, 23 },
            cropped.BgraPixels.Span.ToArray().Where((_, index) => index % 4 == 0).ToArray());
    }
}
