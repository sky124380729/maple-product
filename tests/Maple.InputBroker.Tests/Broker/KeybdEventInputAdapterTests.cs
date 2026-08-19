using Maple.InputBroker;

namespace Maple.InputBroker.Tests.Broker;

public sealed class KeybdEventInputAdapterTests
{
    [Theory]
    [InlineData("Ctrl", 0x11, 0x1D, false)]
    [InlineData("Shift", 0x10, 0x2A, false)]
    [InlineData("Space", 0x20, 0x39, false)]
    [InlineData("A", 0x41, 0x1E, false)]
    [InlineData("S", 0x53, 0x1F, false)]
    [InlineData("D", 0x44, 0x20, false)]
    [InlineData("F", 0x46, 0x21, false)]
    [InlineData("Z", 0x5A, 0x2C, false)]
    [InlineData("X", 0x58, 0x2D, false)]
    [InlineData("C", 0x43, 0x2E, false)]
    [InlineData("V", 0x56, 0x2F, false)]
    [InlineData("Left", 0x25, 0x4B, true)]
    [InlineData("Right", 0x27, 0x4D, true)]
    public void Maps_logical_keys_to_the_verified_windows_encoding(
        string key,
        byte expectedVirtualKey,
        byte expectedScanCode,
        bool expectedExtended)
    {
        bool mapped = KeybdEventInputAdapter.TryMap(
            key,
            out byte virtualKey,
            out byte scanCode,
            out bool extended);

        Assert.True(mapped);
        Assert.Equal(expectedVirtualKey, virtualKey);
        Assert.Equal(expectedScanCode, scanCode);
        Assert.Equal(expectedExtended, extended);
    }

    [Fact]
    public void Rejects_keys_outside_the_product_whitelist()
    {
        Assert.False(KeybdEventInputAdapter.TryMap(
            "Delete",
            out _,
            out _,
            out _));
    }
}
