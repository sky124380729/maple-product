using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Maple.Host.Preview;
using Maple.Host.Recognition;
using WpfImage = System.Windows.Controls.Image;

namespace Maple.WindowsHost.Preview;

public sealed class PreviewWindowHost : IAsyncDisposable
{
    private Window? window;
    private WpfImage? image;
    private TextBlock? diagnostics;
    private Canvas? overlay;
    private PreviewSession? session;
    private RecognitionSession? recognition;
    private IAsyncDisposable? recognitionLease;
    private long firstFrameAtMonoMs;
    private long lastSequence;
    private long displayedFrames;
    private long droppedFrames;
    private int renderPending;

    public event Action<PreviewFault>? Faulted;

    public void Show() => _ = ShowAsync(0, CancellationToken.None);

    public async Task ShowAsync(long hwnd, CancellationToken cancellationToken, bool recognitionEnabled = false)
    {
        if (window is { IsVisible: true })
        {
            window.Activate();
            await session!.StartAsync(hwnd, cancellationToken);
            await SetRecognitionAsync(hwnd, recognitionEnabled, cancellationToken);
            return;
        }

        image = new WpfImage { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
        diagnostics = new TextBlock
        {
            Text = "FPS 0.0   Frame age -   Dropped frames 0",
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(12, 7, 12, 7)
        };
        overlay = new Canvas { IsHitTestVisible = false };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var imageLayer = new Grid();
        imageLayer.Children.Add(image);
        imageLayer.Children.Add(overlay);
        grid.Children.Add(imageLayer);
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
        recognition = new RecognitionSession(new DiagnosticRecognitionProvider());
        recognition.SnapshotPublished += OnRecognitionSnapshot;
        session.FrameArrived += OnFrameArrived;
        session.Faulted += OnFaulted;
        window.Show();
        await session.StartAsync(hwnd, cancellationToken);
        await SetRecognitionAsync(hwnd, recognitionEnabled, cancellationToken);
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
                recognition?.PushFrame(frame);

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
        if (recognitionLease is not null)
        {
            await recognitionLease.DisposeAsync();
            recognitionLease = null;
        }
        if (recognition is not null)
        {
            recognition.SnapshotPublished -= OnRecognitionSnapshot;
            await recognition.DisposeAsync();
            recognition = null;
        }
        image = null;
        overlay = null;
        diagnostics = null;
        firstFrameAtMonoMs = 0;
        lastSequence = 0;
        displayedFrames = 0;
        droppedFrames = 0;
        renderPending = 0;
    }

    private async Task SetRecognitionAsync(long hwnd, bool enabled, CancellationToken cancellationToken)
    {
        if (!enabled || recognition is null) return;
        recognitionLease ??= await recognition.AcquireAsync(
            RecognitionLeaseKind.Preview,
            new Maple.Host.Windows.WindowIdentity(hwnd, 0, string.Empty, 0),
            cancellationToken);
    }

    private void OnRecognitionSnapshot(RecognitionSnapshot snapshot)
    {
        window?.Dispatcher.BeginInvoke(() =>
        {
            if (diagnostics is null) return;
            string hp = snapshot.Hud.HpPercent is double hpValue ? $"HP {hpValue:P0}" : "HP -";
            string mp = snapshot.Hud.MpPercent is double mpValue ? $"MP {mpValue:P0}" : "MP -";
            string exp = snapshot.Hud.ExpPercent is double expValue ? $"EXP {expValue:P1}" : "EXP -";
            diagnostics.Text = $"识别 {snapshot.Health}   {hp}   {mp}   {exp}   怪物 {snapshot.Monsters.Count}   掉落 {snapshot.Drops.Count}   玩家 {snapshot.OtherPlayers.Count}   Frame age {snapshot.FrameAgeMs}ms";
            if (overlay is null) return;
            overlay.Children.Clear();
            foreach (var target in snapshot.Monsters.Concat(snapshot.Drops).Concat(snapshot.OtherPlayers))
            {
                var box = new Border
                {
                    BorderBrush = target.Kind == "monster" ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.Cyan,
                    BorderThickness = new Thickness(2),
                    Width = target.Width,
                    Height = target.Height,
                    Child = new TextBlock { Text = $"{target.Kind} {target.Confidence:P0}", Foreground = System.Windows.Media.Brushes.White }
                };
                Canvas.SetLeft(box, target.X);
                Canvas.SetTop(box, target.Y);
                overlay.Children.Add(box);
            }
        });
    }
}
