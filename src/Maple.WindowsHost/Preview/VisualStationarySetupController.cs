using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Maple.Host.Preview;
using Maple.Host.Stationary;
using WpfImage = System.Windows.Controls.Image;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfPoint = System.Windows.Point;

namespace Maple.WindowsHost.Preview;

internal sealed class VisualStationarySetupController(
    Canvas overlay,
    WpfImage image,
    VisualStationaryProfileStore store,
    Func<CapturedFrame?> latestFrame,
    Action<string> setStatus,
    Action<string> setConfigStatus,
    Action<VisualStationaryProfile> profileSaved)
{
    private SetupStep step;
    private CapturedFrame? frozenFrame;
    private ViewportPoint? dragStart;
    private ViewportPoint? dragCurrent;
    private FrameRect? platform;
    private FrameRect? characterSource;
    private CancellationTokenSource? calibrationCancellation;
    private readonly List<UIElement> visualElements = [];

    public bool IsActive => step != SetupStep.None;
    public VisualStationaryProfile? CurrentProfile { get; private set; }

    public async Task LoadAsync(int frameWidth, int frameHeight)
    {
        VisualProfileLoadResult loaded = await store.LoadAsync(frameWidth, frameHeight, CancellationToken.None);
        if (loaded.Profile is not null)
        {
            CurrentProfile = loaded.Profile;
            setConfigStatus("ready");
            profileSaved(loaded.Profile);
            setStatus("视觉安全区已加载");
        }
        else if (loaded.Code == "VISUAL_VIEWPORT_MISMATCH")
        {
            setConfigStatus("viewportMismatch");
            setStatus("视觉配置与当前画面尺寸不一致，请重新框选");
        }
        else
        {
            setConfigStatus(loaded.Code == "VISUAL_PROFILE_NOT_CONFIGURED" ? "notConfigured" : "invalid");
        }
        RenderOverlay();
    }

    public void BeginPlatformSetup()
    {
        if (!BeginSelection(SetupStep.Platform)) return;
        bool reusesCharacter = CurrentProfile is
        {
            IdentityKind: VisualIdentityKind.CharacterAppearance,
            CharacterAppearance: not null
        };
        setStatus(reusesCharacter
            ? "平台配置：框选新的平台安全范围，已采集人物模板将继续使用"
            : "视觉配置 1/2：框选平台安全范围");
    }

    public void BeginCharacterSetup()
    {
        if (CurrentProfile is null)
        {
            BeginPlatformSetup();
            return;
        }
        if (!BeginSelection(SetupStep.Character)) return;
        platform = CurrentProfile.Platform;
        setStatus("人物模板：框选本人头部和上半身，不含名字、宠物和大范围特效");
        RenderOverlay();
    }

    public void Cancel()
    {
        Detach();
        setStatus(CurrentProfile is null ? "视觉安全区未配置" : "视觉安全区已配置");
        RenderOverlay();
    }

    public void ClearProfile()
    {
        if (IsActive) Detach();
        CurrentProfile = null;
        setConfigStatus("notConfigured");
        setStatus("视觉安全区已清空");
        RenderOverlay();
    }

    public void RenderOverlay(VisualStationaryObservation? observation = null)
    {
        foreach (UIElement element in visualElements) overlay.Children.Remove(element);
        visualElements.Clear();
        CapturedFrame? frame = frozenFrame ?? latestFrame();
        if (frame is null || overlay.ActualWidth <= 0 || overlay.ActualHeight <= 0) return;
        VisualStationaryProfile? profile = CurrentProfile;
        if (profile is not null && !IsActive)
        {
            foreach (VisualPreviewOverlay item in VisualPreviewOverlayLayout.Create(profile, observation))
            {
                (System.Windows.Media.Brush stroke, WpfColor fill) = OverlayStyle(item.Kind);
                AddRectangle(item.Bounds, stroke, fill, 2);
            }
            return;
        }
        FrameRect? platformToDraw = platform ?? profile?.Platform;
        if (platformToDraw.HasValue)
        {
            AddRectangle(platformToDraw.Value, WpfBrushes.Gold,
                WpfColor.FromArgb(35, 255, 196, 0), 2);
            int guard = Math.Max(1, (int)Math.Ceiling(32d * frame.Width / 1366d));
            if (platformToDraw.Value.Width > guard * 2)
            {
                FrameRect safe = platformToDraw.Value with
                {
                    X = platformToDraw.Value.X + guard,
                    Width = platformToDraw.Value.Width - guard * 2
                };
                AddRectangle(safe, WpfBrushes.LimeGreen,
                    WpfColor.FromArgb(24, 50, 205, 90), 2);
            }
        }
        FrameRect? identityToDraw = characterSource ?? profile?.CharacterAppearance?.Source;
        if (!identityToDraw.HasValue && profile is not null &&
            profile.IdentityKind == VisualIdentityKind.NameTemplate)
            identityToDraw = profile.NameSource;
        if (identityToDraw.HasValue)
            AddRectangle(identityToDraw.Value, WpfBrushes.DeepSkyBlue,
                WpfColor.FromArgb(24, 0, 191, 255), 2);
        if (dragStart.HasValue && dragCurrent.HasValue)
        {
            FrameRect? current = MapDrag(dragStart.Value, dragCurrent.Value, frame);
            if (current.HasValue)
                AddRectangle(
                    current.Value,
                    step == SetupStep.Platform ? WpfBrushes.Gold : WpfBrushes.DeepSkyBlue,
                    WpfColor.FromArgb(28, 255, 255, 255),
                    2);
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        WpfPoint point = eventArgs.GetPosition(overlay);
        dragStart = new ViewportPoint(point.X, point.Y);
        dragCurrent = dragStart;
        overlay.CaptureMouse();
        eventArgs.Handled = true;
        RenderOverlay();
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs eventArgs)
    {
        if (!dragStart.HasValue || eventArgs.LeftButton != MouseButtonState.Pressed) return;
        WpfPoint point = eventArgs.GetPosition(overlay);
        dragCurrent = new ViewportPoint(point.X, point.Y);
        RenderOverlay();
    }

    private async void OnMouseUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!dragStart.HasValue || frozenFrame is null) return;
        WpfPoint point = eventArgs.GetPosition(overlay);
        FrameRect? selected = MapDrag(dragStart.Value, new ViewportPoint(point.X, point.Y), frozenFrame);
        overlay.ReleaseMouseCapture();
        dragStart = null;
        dragCurrent = null;
        if (!selected.HasValue)
        {
            setStatus("视觉配置：框选范围无效，请重试");
            RenderOverlay();
            return;
        }
        if (step == SetupStep.Platform)
        {
            if (selected.Value.Width < VisualStationaryProfileValidator.MinimumPlatformWidth)
            {
                setStatus("视觉配置：平台范围太窄，请重新框选");
                RenderOverlay();
                return;
            }
            platform = selected.Value;
            if (CurrentProfile is not null)
            {
                VisualProfileEditResult edited = VisualStationaryProfileEditor.ReplacePlatform(
                    CurrentProfile,
                    selected.Value,
                    frozenFrame.Width,
                    frozenFrame.Height,
                    DateTimeOffset.UtcNow);
                if (edited.Success && edited.Profile is not null)
                {
                    await SaveCompletedProfileAsync(
                        edited.Profile,
                        $"平台安全区已保存，继续使用人物模板 {edited.Profile.CharacterAppearance!.TemplatesBgra.Length} 张",
                        CancellationToken.None);
                    return;
                }
                if (edited.Code != "VISUAL_CHARACTER_TEMPLATE_NOT_CONFIGURED")
                {
                    setStatus("平台配置失败：" + edited.Code);
                    RenderOverlay();
                    return;
                }
            }
            step = SetupStep.Character;
            setStatus("视觉配置 2/2：框选本人头部和上半身，不含名字、宠物和大范围特效");
            RenderOverlay();
            return;
        }

        characterSource = selected.Value;
        CharacterAppearanceCalibrator calibrator;
        try
        {
            calibrator = new CharacterAppearanceCalibrator(frozenFrame, selected.Value);
            VisualStationaryProfile initial = CreateCharacterProfile(calibrator.Complete());
            VisualProfileValidationResult initialValidation = VisualStationaryProfileValidator.Validate(
                initial,
                frozenFrame.Width,
                frozenFrame.Height);
            if (!initialValidation.IsValid)
            {
                setStatus(DescribeValidationFailure(initialValidation.Code));
                RenderOverlay();
                return;
            }

            step = SetupStep.Calibrating;
            overlay.IsHitTestVisible = false;
            setStatus("视觉配置：3 秒后开始采集，请切回游戏并准备左右转向、施法");
            RenderOverlay();
            calibrationCancellation = new CancellationTokenSource();
            CancellationToken token = calibrationCancellation.Token;
            for (int remaining = 3; remaining > 0; remaining--)
            {
                setStatus($"视觉配置：{remaining} 秒后开始，请立即切回游戏");
                await Task.Delay(1000, token);
            }
            const int liveSampleCount = 7;
            for (int sample = 0; sample < liveSampleCount; sample++)
            {
                setStatus($"视觉配置：正在采集人物动作 {sample + 1}/{liveSampleCount}，请左右转向并施法");
                await Task.Delay(857, token);
                CapturedFrame? current = latestFrame();
                if (current is not null) calibrator.TryAdd(current);
            }

            CapturedFrame? finalFrame = latestFrame();
            bool viewportChanged = calibrator.ViewportMismatchDetected ||
                finalFrame is null ||
                finalFrame.Width != frozenFrame.Width ||
                finalFrame.Height != frozenFrame.Height;
            if (viewportChanged || calibrator.ObservedNewFrameCount < 3)
            {
                ResumeCharacterSelection();
                setStatus(viewportChanged
                    ? "视觉配置：采集期间画面尺寸发生变化，请重新框选人物"
                    : "视觉配置：实时画面未持续更新，请确认预览正常后重试");
                RenderOverlay();
                return;
            }

            DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
            VisualStationaryProfile profile = CreateCharacterProfile(
                calibrator.Complete(capturedAt),
                capturedAt);
            VisualProfileValidationResult validation = VisualStationaryProfileValidator.Validate(
                profile,
                frozenFrame.Width,
                frozenFrame.Height);
            if (!validation.IsValid)
            {
                ResumeCharacterSelection();
                setStatus(DescribeValidationFailure(validation.Code));
                RenderOverlay();
                return;
            }

            if (!await SaveCompletedProfileAsync(
                profile,
                $"视觉安全区已保存，人物动作模板 {profile.CharacterAppearance!.TemplatesBgra.Length} 张",
                token))
            {
                ResumeCharacterSelection();
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ResumeCharacterSelection();
            setStatus("视觉配置保存失败：" + exception.GetType().Name);
        }
    }

    private void ResumeCharacterSelection()
    {
        calibrationCancellation?.Dispose();
        calibrationCancellation = null;
        step = SetupStep.Character;
        overlay.IsHitTestVisible = true;
    }

    private VisualStationaryProfile CreateCharacterProfile(
        VisualCharacterTemplateBank bank,
        DateTimeOffset? updatedAtUtc = null) => new(
        VisualStationaryProfile.SchemaVersionCurrent,
        frozenFrame!.Width,
        frozenFrame.Height,
        platform!.Value,
        new FrameRect(0, 0, 0, 0),
        0,
        0,
        [],
        updatedAtUtc ?? DateTimeOffset.UtcNow,
        VisualIdentityKind.CharacterAppearance,
        bank);

    private bool BeginSelection(SetupStep targetStep)
    {
        CapturedFrame? frame = latestFrame();
        if (frame is null)
        {
            setStatus("视觉配置：等待预览画面");
            return false;
        }
        if (IsActive) Detach();
        frozenFrame = frame with { BgraPixels = frame.BgraPixels.ToArray() };
        var bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(
            new Int32Rect(0, 0, frame.Width, frame.Height),
            frozenFrame.BgraPixels.ToArray(),
            frame.Stride,
            0);
        image.Source = bitmap;
        platform = null;
        characterSource = null;
        dragStart = null;
        dragCurrent = null;
        step = targetStep;
        overlay.IsHitTestVisible = true;
        overlay.Cursor = WpfCursors.Cross;
        overlay.MouseLeftButtonDown += OnMouseDown;
        overlay.MouseMove += OnMouseMove;
        overlay.MouseLeftButtonUp += OnMouseUp;
        RenderOverlay();
        return true;
    }

    private async Task<bool> SaveCompletedProfileAsync(
        VisualStationaryProfile profile,
        string successStatus,
        CancellationToken cancellationToken)
    {
        VisualProfileSaveResult saved = await store.SaveAsync(profile, cancellationToken);
        if (!saved.Success)
        {
            setStatus("视觉配置失败：" + saved.Code);
            return false;
        }
        CurrentProfile = profile;
        setConfigStatus("ready");
        profileSaved(profile);
        Detach();
        setStatus(successStatus);
        RenderOverlay();
        return true;
    }

    private static string DescribeValidationFailure(string code) => code switch
    {
        "VISUAL_CHARACTER_TEMPLATE_TOO_SMALL" => "视觉配置：人物框太小，请重新框选头部和上半身",
        "VISUAL_CHARACTER_TEMPLATE_TOO_LARGE" => "视觉配置：人物框太大，请只框头部和上半身",
        "VISUAL_CHARACTER_TEMPLATE_LOW_TEXTURE" => "视觉配置：人物区域特征不足，请避开纯色背景后重试",
        _ => "视觉配置失败：" + code
    };

    private FrameRect? MapDrag(ViewportPoint start, ViewportPoint end, CapturedFrame frame) =>
        VisualSetupGeometry.MapDragToFrame(
            start,
            end,
            overlay.ActualWidth > 0 ? overlay.ActualWidth : image.ActualWidth,
            overlay.ActualHeight > 0 ? overlay.ActualHeight : image.ActualHeight,
            frame.Width,
            frame.Height);

    private void AddRectangle(
        FrameRect frameRectangle,
        System.Windows.Media.Brush stroke,
        System.Windows.Media.Color fill,
        double thickness)
    {
        CapturedFrame? frame = frozenFrame ?? latestFrame();
        if (frame is null) return;
        (double x, double y, double width, double height) = VisualSetupGeometry.MapFrameRectToViewport(
            frameRectangle,
            overlay.ActualWidth,
            overlay.ActualHeight,
            frame.Width,
            frame.Height);
        var rectangle = new WpfRectangle
        {
            Width = width,
            Height = height,
            Stroke = stroke,
            StrokeThickness = thickness,
            Fill = new SolidColorBrush(fill),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        overlay.Children.Add(rectangle);
        visualElements.Add(rectangle);
    }

    private static (System.Windows.Media.Brush Stroke, WpfColor Fill)
        OverlayStyle(VisualPreviewOverlayKind kind) => kind switch
        {
            VisualPreviewOverlayKind.PlatformBoundary =>
                (WpfBrushes.Gold, WpfColor.FromArgb(35, 255, 196, 0)),
            VisualPreviewOverlayKind.SafeInterior =>
                (WpfBrushes.LimeGreen, WpfColor.FromArgb(24, 50, 205, 90)),
            VisualPreviewOverlayKind.CharacterTemplate =>
                (WpfBrushes.DeepSkyBlue, WpfColor.FromArgb(24, 0, 191, 255)),
            VisualPreviewOverlayKind.TrustedIdentity =>
                (WpfBrushes.Cyan, WpfColor.FromArgb(30, 0, 255, 255)),
            _ =>
                (WpfBrushes.Orange, WpfColor.FromArgb(28, 255, 165, 0))
        };

    private void Detach()
    {
        calibrationCancellation?.Cancel();
        calibrationCancellation?.Dispose();
        calibrationCancellation = null;
        overlay.MouseLeftButtonDown -= OnMouseDown;
        overlay.MouseMove -= OnMouseMove;
        overlay.MouseLeftButtonUp -= OnMouseUp;
        overlay.ReleaseMouseCapture();
        overlay.IsHitTestVisible = false;
        overlay.Cursor = WpfCursors.Arrow;
        step = SetupStep.None;
        frozenFrame = null;
        platform = null;
        characterSource = null;
        dragStart = null;
        dragCurrent = null;
    }

    private enum SetupStep { None, Platform, Character, Calibrating }
}
