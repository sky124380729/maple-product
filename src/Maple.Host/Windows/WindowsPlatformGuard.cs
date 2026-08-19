namespace Maple.Host.Windows;

public sealed record PlatformSupportResult(bool Supported, string Code)
{
    public static PlatformSupportResult SupportedResult() => new(true, "WINDOWS_PLATFORM_SUPPORTED");
    public static PlatformSupportResult Rejected(string code) => new(false, code);
}

public static class WindowsPlatformGuard
{
    public static PlatformSupportResult Check()
    {
        if (!OperatingSystem.IsWindows()) return PlatformSupportResult.Rejected("WINDOWS_REQUIRED");
        Version version = Environment.OSVersion.Version;
        return version.Build >= 19_045
            ? PlatformSupportResult.SupportedResult()
            : PlatformSupportResult.Rejected("WINDOWS_10_22H2_REQUIRED");
    }
}
