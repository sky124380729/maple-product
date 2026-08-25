using System.Runtime.InteropServices;

namespace Maple.InputBroker;

public sealed class KeybdEventInputAdapter : IBrokerKeySender
{
    private const uint KeyEventFExtendedKey = 0x0001;
    private const uint KeyEventFKeyUp = 0x0002;

    public bool Send(string key, bool isKeyUp)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("keybd_event is Windows-only.");
        if (!TryMap(key, out byte virtualKey, out byte scanCode, out bool extended)) return false;
        uint flags = (isKeyUp ? KeyEventFKeyUp : 0) | (extended ? KeyEventFExtendedKey : 0);
        keybd_event(virtualKey, scanCode, flags, UIntPtr.Zero);
        return true;
    }

    internal static bool TryMap(
        string key,
        out byte virtualKey,
        out byte scanCode,
        out bool extended)
    {
        (virtualKey, scanCode, extended) = key.ToUpperInvariant() switch
        {
            "CTRL" => ((byte)0x11, (byte)0x1D, false),
            "SHIFT" => ((byte)0x10, (byte)0x2A, false),
            "SPACE" => ((byte)0x20, (byte)0x39, false),
            "A" => ((byte)0x41, (byte)0x1E, false),
            "S" => ((byte)0x53, (byte)0x1F, false),
            "D" => ((byte)0x44, (byte)0x20, false),
            "F" => ((byte)0x46, (byte)0x21, false),
            "Z" => ((byte)0x5A, (byte)0x2C, false),
            "X" => ((byte)0x58, (byte)0x2D, false),
            "C" => ((byte)0x43, (byte)0x2E, false),
            "V" => ((byte)0x56, (byte)0x2F, false),
            "LEFT" => ((byte)0x25, (byte)0x4B, true),
            "RIGHT" => ((byte)0x27, (byte)0x4D, true),
            "UP" => ((byte)0x26, (byte)0x48, true),
            "DOWN" => ((byte)0x28, (byte)0x50, true),
            _ => ((byte)0, (byte)0, false)
        };
        return virtualKey != 0;
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
