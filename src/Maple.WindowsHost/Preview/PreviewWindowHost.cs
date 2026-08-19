using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Maple.Host.Preview;
using WpfImage = System.Windows.Controls.Image;

namespace Maple.WindowsHost.Preview;

public sealed class PreviewWindowHost : IAsyncDisposable
{
    private Window? window;
    private WpfImage? image;
    private TextBlock? diagnostics;
    private PreviewSession? session;
    private long firstFrameAtMonoMs;
    private long lastSequence;
    private long displayedFrames;
    private long droppedFrames;
    private int renderPending;

    public event Action<PreviewFault>? Faulted;

    public void Show() => _ = ShowAsync(0, CancellationToken.None);

    public async Task ShowAsync(long hwnd, CancellationToken cancellationToken)
    {
        if (window is { IsVisible: true })
        {
            window.Activate();
            await session!.StartAsync(hwnd, cancellationToken);
            return;
        }

        image = new WpfImage { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
        diagnostics = new TextBlock
        {
            Text = "FPS 0.0   Frame age -   Dropped frames 0",
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(12, 7, 12, 7)
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(image);
        var diagnosticsBar = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(224, 20, 25, 24)),
            Child = diagnostics
        };
        Grid.SetRow(diagnosticsBar, 1);
        grid.Children.Add(diagnosticsBar);

        window = new Window
        {
            Title = "Maple Product 实时预览",
            Width = 900,
            Height = 560,
            MinWidth = 640,
            MinHeight = 420,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 31, 30)),
            Content = grid
        };
        window.Closed += OnClosed;
        session = new PreviewSession(new WindowsGraphicsCaptureSource());
        session.FrameArrived += OnFrameArrived;
        session.Faulted += OnFaulted;
        window.Show();
        await session.StartAsync(hwnd, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Window? activeWindow = window;
        window = null;
        if (activeWindow is not null)
        {
            activeWindow.Closed -= OnClosed;
            activeWindow.Close();
        }
        await DisposeSessionAsync();
    }

    private void OnFrameArrived(CapturedFrame frame)
    {
        if (window is null) return;
        if (Interlocked.Exchange(ref renderPending, 1) != 0) return;
        window.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (window is null || image is null || diagnostics is null) return;
                var bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
                bitmap.WritePixels(
                    new Int32Rect(0, 0, frame.Width, frame.Height),
                    frame.BgraPixels.ToArray(),
                    frame.Stride,
                    0);
                image.Source = bitmap;

                if (firstFrameAtMonoMs == 0) firstFrameAtMonoMs = frame.CapturedAtMonoMs;
                if (lastSequence > 0 && frame.Sequence > lastSequence + 1)
                    droppedFrames += frame.Sequence - lastSequence - 1;
                lastSequence = frame.Sequence;
                displayedFrames++;
                long elapsed = Math.Max(1, frame.CapturedAtMonoMs - firstFrameAtMonoMs);
                double fps = displayedFrames * 1000d / elapsed;
                long age = Math.Max(0, Environment.TickCount64 - frame.CapturedAtMonoMs);
                diagnostics.Text = $"FPS {fps:F1}   Frame age {age}ms   Dropped frames {droppedFrames}";
            }
            finally { Volatile.Write(ref renderPending, 0); }
        });
    }

    private void OnFaulted(PreviewFault fault)
    {
        window?.Dispatcher.BeginInvoke(() =>
        {
            if (diagnostics is not null) diagnostics.Text = fault.Code;
        });
        Faulted?.Invoke(fault);
    }

    private async void OnClosed(object? sender, EventArgs eventArgs)
    {
        window = null;
        await DisposeSessionAsync();
    }

    private async Task DisposeSessionAsync()
    {
        PreviewSession? activeSession = session;
        session = null;
        if (activeSession is not null)
        {
            activeSession.FrameArrived -= OnFrameArrived;
            activeSession.Faulted -= OnFaulted;
            await activeSession.DisposeAsync();
        }
        image = null;
        diagnostics = null;
        firstFrameAtMonoMs = 0;
        lastSequence = 0;
        displayedFrames = 0;
        droppedFrames = 0;
        renderPending = 0;
    }
}
