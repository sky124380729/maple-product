namespace Maple.Host.Stationary;

public sealed class VisualPlatformSafetyGate
{
    private readonly FrameRect platform;

    public VisualPlatformSafetyGate(FrameRect platform, int frameWidth)
    {
        this.platform = platform;
        GuardWidthPx = Math.Max(1, (int)Math.Ceiling(32d * frameWidth / 1366d));
    }

    public int GuardWidthPx { get; private set; }

    public VisualPlatformState ObserveTrusted(long sequence, double centerX, double bestScore)
    {
        int offset = (int)Math.Round(centerX - (platform.X + platform.Width / 2d));
        if (GuardWidthPx * 2 >= platform.Width)
            return new VisualPlatformState(
                VisualSafetyState.Untrusted,
                sequence,
                centerX,
                bestScore,
                GuardWidthPx,
                offset,
                "VISUAL_PLATFORM_GUARD_EXHAUSTED");

        VisualSafetyState state = centerX < platform.X || centerX > platform.Right
            ? VisualSafetyState.Outside
            : centerX < platform.X + GuardWidthPx
                ? VisualSafetyState.GuardLeft
                : centerX > platform.Right - GuardWidthPx
                    ? VisualSafetyState.GuardRight
                    : VisualSafetyState.Safe;
        return new VisualPlatformState(state, sequence, centerX, bestScore, GuardWidthPx, offset, Code(state));
    }

    public VisualPlatformState ObserveUntrusted(long sequence, double bestScore, string code) =>
        new(VisualSafetyState.Untrusted, sequence, null, bestScore, GuardWidthPx, null, code);

    public void RecordMovement(double beforeX, double afterX, double jitterPx)
    {
        int required = (int)Math.Ceiling(Math.Abs(afterX - beforeX) + Math.Max(0, jitterPx) * 3);
        GuardWidthPx = Math.Max(GuardWidthPx, required);
    }

    private static string Code(VisualSafetyState state) => state switch
    {
        VisualSafetyState.Safe => "VISUAL_SAFE",
        VisualSafetyState.GuardLeft => "VISUAL_GUARD_LEFT",
        VisualSafetyState.GuardRight => "VISUAL_GUARD_RIGHT",
        VisualSafetyState.Outside => "VISUAL_OUTSIDE_FROZEN",
        _ => "VISUAL_UNTRUSTED_FROZEN"
    };
}
