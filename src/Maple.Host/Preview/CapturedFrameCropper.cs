namespace Maple.Host.Preview;

public static class CapturedFrameCropper
{
    public static CapturedFrame Crop(CapturedFrame frame, int left, int top, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frame);
        left = Math.Clamp(left, 0, frame.Width - 1);
        top = Math.Clamp(top, 0, frame.Height - 1);
        width = Math.Clamp(width, 1, frame.Width - left);
        height = Math.Clamp(height, 1, frame.Height - top);
        if (left == 0 && top == 0 && width == frame.Width && height == frame.Height) return frame;

        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        ReadOnlySpan<byte> source = frame.BgraPixels.Span;
        for (int row = 0; row < height; row++)
            source.Slice((top + row) * frame.Stride + left * 4, stride)
                .CopyTo(pixels.AsSpan(row * stride, stride));
        return frame with { Width = width, Height = height, Stride = stride, BgraPixels = pixels };
    }
}
