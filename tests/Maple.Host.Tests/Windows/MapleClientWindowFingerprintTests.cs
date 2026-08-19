using Maple.Host.Windows;

namespace Maple.Host.Tests.Windows;

public sealed class MapleClientWindowFingerprintTests
{
    [Theory]
    [InlineData(false, "冒险岛怀旧服", "UnityWndClass")]
    [InlineData(true, "冒险岛怀旧服 - 1", "UnityWndClass")]
    [InlineData(true, "冒险岛怀旧服", "OtherClass")]
    [InlineData(true, "MapleStory", "UnityWndClass")]
    public void Rejects_windows_that_do_not_match_the_exact_fingerprint(
        bool visible,
        string title,
        string className)
    {
        Assert.False(MapleClientWindowFingerprint.Matches(visible, title, className));
    }

    [Fact]
    public void Accepts_the_visible_classic_client_window()
    {
        Assert.True(MapleClientWindowFingerprint.Matches(
            visible: true,
            title: "冒险岛怀旧服",
            className: "UnityWndClass"));
    }
}
