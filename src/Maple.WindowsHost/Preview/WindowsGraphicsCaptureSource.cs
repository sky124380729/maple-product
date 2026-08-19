using System.Runtime.InteropServices;
using Maple.Host.Preview;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using D3D11Device = SharpDX.Direct3D11.Device;

namespace Maple.WindowsHost.Preview;

public sealed class WindowsGraphicsCaptureSource : IFrameCaptureSource
{
    private D3D11Device? nativeDevice;
    private IDirect3DDevice? winrtDevice;
    private Direct3D11CaptureFramePool? framePool;
    private GraphicsCaptureSession? captureSession;
    private long sequence;
    private long lastEmittedMonoMs;

    public event Action<CapturedFrame>? FrameArrived;
    public event Action<PreviewFault>? Faulted;

    public Task StartAsync(long hwnd, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!GraphicsCaptureSession.IsSupported())
            throw new PlatformNotSupportedException("Windows.Graphics.Capture is unavailable.");

        StopCore();
        nativeDevice = new D3D11Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        winrtDevice = Direct3DInterop.CreateWinRtDevice(nativeDevice);
        GraphicsCaptureItem item = CaptureItemInterop.CreateForWindow(new IntPtr(hwnd));
        framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            winrtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            item.Size);
        framePool.FrameArrived += OnFrameArrived;
        captureSession = framePool.CreateCaptureSession(item);
        captureSession.IsCursorCaptureEnabled = false;
        captureSession.StartCapture();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCore();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        StopCore();
        return ValueTask.CompletedTask;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            using Direct3D11CaptureFrame frame = sender.TryGetNextFrame();
            long current = Interlocked.Increment(ref sequence);
            long capturedAt = Environment.TickCount64;
            if (capturedAt - Interlocked.Read(ref lastEmittedMonoMs) < 33) return;
            Interlocked.Exchange(ref lastEmittedMonoMs, capturedAt);
            using Texture2D source = Direct3DInterop.CreateTexture(frame.Surface);
            Texture2DDescription description = source.Description;
            description.BindFlags = BindFlags.None;
            description.CpuAccessFlags = CpuAccessFlags.Read;
            description.OptionFlags = ResourceOptionFlags.None;
            description.Usage = ResourceUsage.Staging;
            using var staging = new Texture2D(nativeDevice!, description);
            nativeDevice!.ImmediateContext.CopyResource(source, staging);
            DataBox mapped = nativeDevice.ImmediateContext.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
            try
            {
                int rowBytes = description.Width * 4;
                byte[] pixels = new byte[rowBytes * description.Height];
                for (int row = 0; row < description.Height; row++)
                    Marshal.Copy(IntPtr.Add(mapped.DataPointer, row * mapped.RowPitch), pixels, row * rowBytes, rowBytes);
                FrameArrived?.Invoke(new CapturedFrame(
                    description.Width,
                    description.Height,
                    rowBytes,
                    pixels,
                    capturedAt,
                    current));
            }
            finally
            {
                nativeDevice.ImmediateContext.UnmapSubresource(staging, 0);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Faulted?.Invoke(new PreviewFault("PREVIEW_FRAME_FAILED:" + exception.GetType().Name));
        }
    }

    private void StopCore()
    {
        if (framePool is not null) framePool.FrameArrived -= OnFrameArrived;
        captureSession?.Dispose();
        framePool?.Dispose();
        nativeDevice?.Dispose();
        captureSession = null;
        framePool = null;
        winrtDevice = null;
        nativeDevice = null;
        sequence = 0;
        lastEmittedMonoMs = 0;
    }
}

internal static class CaptureItemInterop
{
    private static readonly Guid ItemInterfaceId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
    {
        IGraphicsCaptureItemInterop interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        IntPtr pointer = interop.CreateForWindow(hwnd, ItemInterfaceId);
        try { return GraphicsCaptureItem.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, in Guid iid);
        IntPtr CreateForMonitor(IntPtr monitor, in Guid iid);
    }
}

internal static class Direct3DInterop
{
    private static readonly Guid TextureInterfaceId = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    public static IDirect3DDevice CreateWinRtDevice(D3D11Device device)
    {
        using var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();
        int result = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr pointer);
        Marshal.ThrowExceptionForHR(result);
        try { return MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    public static Texture2D CreateTexture(IDirect3DSurface surface)
    {
        IDirect3DDxgiInterfaceAccess access = surface.As<IDirect3DDxgiInterfaceAccess>();
        return new Texture2D(access.GetInterface(TextureInterfaceId));
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface(in Guid iid);
    }
}
