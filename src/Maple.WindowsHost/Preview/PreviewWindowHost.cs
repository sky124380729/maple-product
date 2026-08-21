using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Maple.Host.Navigation;
using Maple.Host.Preview;
using Maple.Host.Recognition;
using WpfImage = System.Windows.Controls.Image;
using WpfButton = System.Windows.Controls.Button;

namespace Maple.WindowsHost.Preview;

public sealed class PreviewWindowHost : IAsyncDisposable
{
    private Window? window;
    private WpfImage? image;
    private TextBlock? diagnostics;
    private TextBlock? recordingStatus;
    private WpfButton? recordButton;
    private Canvas? overlay;
    private PreviewSession? session;
    private RecognitionSession? recognition;
    private RecognitionLeaseToggle? recognitionToggle;
    private long firstFrameAtMonoMs;
    private long lastSequence;
    private long displayedFrames;
    private long droppedFrames;
    private int renderPending;
    private RecognitionSnapshot? latestRecognition;
    private int lastFrameWidth;
    private int lastFrameHeight;
    private bool recognitionRequested;
    private readonly SemaphoreSlim recordingGate = new(1, 1);
    private int recordingStopPending;

    public event Action<PreviewFault>? Faulted;
    public event Action<RecognitionSnapshot>? RecognitionSnapshotPublished;

    public void Show() => _ = ShowAsync(0, CancellationToken.None);

    public async Task ShowAsync(long hwnd, CancellationToken cancellationToken, bool recognitionEnabled = false, bool startRecording = false)
    {
        if (window is { IsVisible: true })
        {
            recognitionRequested = recognitionEnabled;
            window.Activate();
            await session!.StartAsync(hwnd, cancellationToken);
            await SetRecognitionAsync(hwnd, recognitionEnabled || startRecording || recorder is not null, cancellationToken);
            if (startRecording && recorder is null) await StartRecordingAsync();
            return;
        }

        image = new WpfImage { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
        diagnostics = new TextBlock
        {
            Text = "FPS 0.0   Frame age -   Dropped frames 0",
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(12, 7, 12, 7)
        };
        recordingStatus = new TextBlock
        {
            Text = "地图录制未开始",
            Foreground = System.Windows.Media.Brushes.LightGray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };
        recordButton = new WpfButton
        {
            Content = "开始录制地图",
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(12, 5, 0, 5)
        };
        recordButton.Click += OnRecordClicked;
        overlay = new Canvas { IsHitTestVisible = false };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var toolbar = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        toolbar.Children.Add(recordButton);
        toolbar.Children.Add(recordingStatus);
        grid.Children.Add(toolbar);
        var imageLayer = new Grid();
        imageLayer.Children.Add(image);
        imageLayer.Children.Add(overlay);
        Grid.SetRow(imageLayer, 1);
        grid.Children.Add(imageLayer);
        var diagnosticsBar = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(224, 20, 25, 24)),
            Child = diagnostics
        };
        Grid.SetRow(diagnosticsBar, 2);
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
        recognition = new RecognitionSession(RecognitionProviderFactory.Create());
        recognitionRequested = recognitionEnabled;
        recognitionToggle = new RecognitionLeaseToggle(token => recognition.AcquireAsync(
            RecognitionLeaseKind.Preview,
            new Maple.Host.Windows.WindowIdentity(hwnd, 0, string.Empty, 0),
            token));
        recognition.SnapshotPublished += OnRecognitionSnapshot;
        session.FrameArrived += OnFrameArrived;
        session.Faulted += OnFaulted;
        window.Show();
        await session.StartAsync(hwnd, cancellationToken);
        await SetRecognitionAsync(hwnd, recognitionEnabled || startRecording, cancellationToken);
        if (startRecording) await StartRecordingAsync();
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
                lastFrameWidth = frame.Width;
                lastFrameHeight = frame.Height;
                recognition?.PushFrame(frame);
                MapRecorder? activeRecorder = recorder;
                if (activeRecorder is not null)
                {
                    RecognitionSnapshot? recognizedFrame = latestRecognition;
                    MapLocalObservation? local = CreateLocalObservation(recognizedFrame, frame);
                    MapRecordingStatus status = activeRecorder.PushFrame(
                        frame,
                        MinimapGeometryDetector.Observe(frame),
                        local);
                    recordingStatus!.Text = $"录制中：样本 {status.SampleCount}，平台 {status.PlatformCandidateCount}，梯子 {status.LadderCandidateCount}";
                    if (!status.IsRecording && status.StopReason is not null)
                        RequestRecordingStop(status.StopReason);
                }
                RenderRecognitionOverlay();

                if (firstFrameAtMonoMs == 0) firstFrameAtMonoMs = frame.CapturedAtMonoMs;
                if (lastSequence > 0 && frame.Sequence > lastSequence + 1)
                    droppedFrames += frame.Sequence - lastSequence - 1;
                lastSequence = frame.Sequence;
                displayedFrames++;
                long elapsed = Math.Max(1, frame.CapturedAtMonoMs - firstFrameAtMonoMs);
                double fps = displayedFrames * 1000d / elapsed;
                long age = Math.Max(0, Environment.TickCount64 - frame.CapturedAtMonoMs);
                RecognitionSnapshot? recognized = latestRecognition;
                string recognitionText = recognized is null
                    ? "识别未开启"
                    : $"识别 {recognized.Health}   人物 {(recognized.Self is null ? 0 : 1)}   怪物 {recognized.Monsters.Count}";
                diagnostics.Text = $"FPS {fps:F1}   Frame age {age}ms   Dropped {droppedFrames}   {recognitionText}";
            }
            finally { Volatile.Write(ref renderPending, 0); }
        });
    }

    private void OnFaulted(PreviewFault fault)
    {
        window?.Dispatcher.BeginInvoke(() =>
        {
            if (diagnostics is not null) diagnostics.Text = fault.Code;
            if (recorder is not null) RequestRecordingStop("CAPTURE_FAULT");
        });
        Faulted?.Invoke(fault);
    }

    private async void OnClosed(object? sender, EventArgs eventArgs)
    {
        window = null;
        await DisposeSessionAsync();
    }

    private MapRecorder? recorder;

    private async void OnRecordClicked(object? sender, RoutedEventArgs eventArgs)
    {
        try
        {
            if (recordButton is not null) recordButton.IsEnabled = false;
            if (recorder is null) await StartRecordingAsync();
            else await StopRecordingAsync("OPERATOR_STOPPED");
        }
        catch (Exception exception)
        {
            if (recordingStatus is not null) recordingStatus.Text = "录制操作失败：" + exception.Message;
        }
        finally
        {
            if (recordButton is not null) recordButton.IsEnabled = true;
        }
    }

    private async Task StartRecordingAsync()
    {
        await recordingGate.WaitAsync();
        try
        {
            if (recorder is not null) return;
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MapleProduct", "map-recordings");
            var created = new MapRecorder(new MapRecordingOptions("current-map", directory));
            created.Start(Environment.TickCount64);
            try
            {
                if (recognitionToggle is not null)
                    await recognitionToggle.SetEnabledAsync(true, CancellationToken.None);
                recorder = created;
                Volatile.Write(ref recordingStopPending, 0);
                if (recordButton is not null) recordButton.Content = "结束录制地图";
                if (recordingStatus is not null) recordingStatus.Text = "录制中：请手动走过平台和梯子";
            }
            catch
            {
                await created.DisposeAsync();
                throw;
            }
        }
        finally
        {
            recordingGate.Release();
        }
    }

    public async Task StopRecordingAsync(string reason = "OPERATOR_STOPPED")
    {
        await recordingGate.WaitAsync();
        try
        {
            MapRecorder? active = recorder;
            if (active is null) return;
            recorder = null;
            try
            {
                MapRecordingResult result = await active.StopAsync(reason);
                string quality = result.PlanningReady
                    ? "可用于规划"
                    : "需要继续录制：" + string.Join(",", result.QualityReasons);
                if (recordingStatus is not null)
                    recordingStatus.Text = $"录制完成：{result.SampleCount} 个样本，{quality}，包已保存到 {result.PackagePath}";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or MapPackageLoadException)
            {
                if (recordingStatus is not null)
                    recordingStatus.Text = "录制导出失败：" + exception.Message;
            }
            finally
            {
                await active.DisposeAsync();
                if (!recognitionRequested)
                    await SetRecognitionAsync(0, false, CancellationToken.None);
                if (recordButton is not null) recordButton.Content = "开始录制地图";
            }
        }
        finally
        {
            Volatile.Write(ref recordingStopPending, 0);
            recordingGate.Release();
        }
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
        await StopRecordingSafelyAsync("PREVIEW_CLOSED");
        if (recognitionToggle is not null)
        {
            await recognitionToggle.DisposeAsync();
            recognitionToggle = null;
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
        recordingStatus = null;
        recordButton = null;
        firstFrameAtMonoMs = 0;
        lastSequence = 0;
        displayedFrames = 0;
        droppedFrames = 0;
        renderPending = 0;
        latestRecognition = null;
        lastFrameWidth = 0;
        lastFrameHeight = 0;
    }

    private async Task SetRecognitionAsync(long hwnd, bool enabled, CancellationToken cancellationToken)
    {
        if (recognitionToggle is null) return;
        await recognitionToggle.SetEnabledAsync(enabled, cancellationToken);
        if (!enabled)
        {
            latestRecognition = null;
            overlay?.Children.Clear();
        }
    }

    private void OnRecognitionSnapshot(RecognitionSnapshot snapshot)
    {
        latestRecognition = snapshot;
        RecognitionSnapshotPublished?.Invoke(snapshot);
        window?.Dispatcher.BeginInvoke(() => RenderRecognitionOverlay());
    }

    private void RequestRecordingStop(string reason)
    {
        if (Interlocked.CompareExchange(ref recordingStopPending, 1, 0) != 0) return;
        _ = StopRecordingSafelyAsync(reason);
    }

    private async Task StopRecordingSafelyAsync(string reason)
    {
        try
        {
            await StopRecordingAsync(reason);
        }
        catch (Exception exception)
        {
            if (recordingStatus is not null) recordingStatus.Text = "录制停止失败：" + exception.Message;
        }
    }

    private static MapLocalObservation? CreateLocalObservation(
        RecognitionSnapshot? recognized,
        CapturedFrame frame)
    {
        if (recognized is null
            || recognized.Health != RecognitionHealth.Running
            || recognized.Geometry is null
            || recognized.FrameWidth <= 0
            || recognized.FrameHeight <= 0
            || recognized.CapturedAtMonoMs > frame.CapturedAtMonoMs
            || frame.CapturedAtMonoMs - recognized.CapturedAtMonoMs > 1000)
            return null;
        MapLocalSelf? self = recognized.Self is null
            ? null
            : new MapLocalSelf(
                recognized.Self.X / recognized.FrameWidth,
                recognized.Self.Y / recognized.FrameHeight,
                recognized.Self.Width / recognized.FrameWidth,
                recognized.Self.Height / recognized.FrameHeight);
        return new MapLocalObservation(recognized.Geometry, self, recognized.FrameSequence);
    }

    private void RenderRecognitionOverlay()
    {
        if (overlay is null || latestRecognition is null) return;
        double viewportWidth = overlay.ActualWidth > 0 ? overlay.ActualWidth : image?.ActualWidth ?? 0;
        double viewportHeight = overlay.ActualHeight > 0 ? overlay.ActualHeight : image?.ActualHeight ?? 0;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;
        overlay.Children.Clear();
        IReadOnlyList<RecognitionOverlayBox> boxes = RecognitionOverlayLayout.Create(
            latestRecognition,
            lastFrameWidth,
            lastFrameHeight,
            viewportWidth,
            viewportHeight);
        foreach (RecognitionOverlayBox target in boxes)
        {
            var box = new Border
            {
                BorderBrush = target.Kind == "monster"
                    ? System.Windows.Media.Brushes.Red
                    : System.Windows.Media.Brushes.LimeGreen,
                BorderThickness = new Thickness(2),
                Width = target.Width,
                Height = target.Height,
                Child = new TextBlock
                {
                    Text = $"{(target.Kind == "monster" ? "怪物" : "人物")} {target.Confidence:P0}",
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 0, 0, 0))
                }
            };
            Canvas.SetLeft(box, target.X);
            Canvas.SetTop(box, target.Y);
            overlay.Children.Add(box);
        }
    }
}
