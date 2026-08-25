using Maple.Host.Preview;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class VisualStationarySetupTests
{
    [Fact]
    public void Removes_uniform_stretch_letterboxing_when_mapping_drag_to_frame()
    {
        FrameRect? mapped = VisualSetupGeometry.MapDragToFrame(
            new ViewportPoint(50, 75),
            new ViewportPoint(250, 175),
            viewportWidth: 400,
            viewportHeight: 300,
            frameWidth: 160,
            frameHeight: 80);

        Assert.Equal(new FrameRect(20, 10, 80, 40), mapped);
    }

    [Fact]
    public void Normalizes_reverse_drag_and_clamps_to_displayed_frame()
    {
        FrameRect? mapped = VisualSetupGeometry.MapDragToFrame(
            new ViewportPoint(500, 260),
            new ViewportPoint(-20, 40),
            viewportWidth: 400,
            viewportHeight: 300,
            frameWidth: 160,
            frameHeight: 80);

        Assert.Equal(new FrameRect(0, 0, 160, 80), mapped);
    }

    [Fact]
    public void Crops_the_exact_bgra_name_pixels_selected_in_viewport()
    {
        byte[] pixels = new byte[16 * 8 * 4];
        for (int index = 0; index < pixels.Length; index++) pixels[index] = (byte)(index % 251);
        var frame = new CapturedFrame(16, 8, 64, pixels, 10, 1);
        FrameRect selected = VisualSetupGeometry.MapDragToFrame(
            new ViewportPoint(20, 10),
            new ViewportPoint(60, 30),
            160,
            80,
            16,
            8)!.Value;

        CapturedFrame cropped = CapturedFrameCropper.Crop(
            frame, selected.X, selected.Y, selected.Width, selected.Height);

        Assert.Equal(new FrameRect(2, 1, 4, 2), selected);
        Assert.Equal(pixels.AsSpan(1 * 64 + 2 * 4, 16).ToArray(), cropped.BgraPixels.Span[..16].ToArray());
    }
}
