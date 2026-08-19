using System.Runtime.InteropServices;

namespace Maple.InputBroker;

public sealed class KeybdEventInputAdapter : IBrokerKeySender
{
    private const uint KeyEventFExtendedKey = 0x0001;
    private const uint KeyEventFKeyUp = 0x0002;

    public bool Send(string key, bool isKeyUp)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("keybd_event is Windows-only.");
        if (!TryMap(key, out byte virtualKey, out bool extended)) return false;
        uint flags = (isKeyUp ? KeyEventFKeyUp : 0) | (extended ? KeyEventFExtendedKey : 0);
        keybd_event(virtualKey, 0, flags, UIntPtr.Zero);
        return true;
    }

    private static bool TryMap(string key, out byte virtualKey, out bool extended)
    {
        extended = false;
        virtualKey = key.ToUpperInvariant() switch
        {
            "CTRL" => 0x11,
            "SHIFT" => 0x10,
            "SPACE" => 0x20,
            "A" => 0x41,
            "S" => 0x53,
            "D" => 0x44,
            "F" => 0x46,
            "Z" => 0x5A,
            "X" => 0x58,
            "C" => 0x43,
            "V" => 0x56,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            _ => 0
        };
        extended = key.Equals("Left", StringComparison.OrdinalIgnoreCase) ||
                   key.Equals("Right", StringComparison.OrdinalIgnoreCase);
        return virtualKey != 0;
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
