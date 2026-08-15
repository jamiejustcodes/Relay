using System.Windows.Input;
using ScreenLens.Infrastructure.ScreenCapture;

namespace ScreenLens.Infrastructure.Hotkeys;

public static class HotkeyParser
{
    public static (uint Modifiers, uint KeyCode) Parse(string modifiersStr, string keyStr)
    {
        uint modifiers = 0;

        if (modifiersStr.Contains("Control", StringComparison.OrdinalIgnoreCase))
            modifiers |= NativeMethods.MOD_CONTROL;
        if (modifiersStr.Contains("Alt", StringComparison.OrdinalIgnoreCase))
            modifiers |= NativeMethods.MOD_ALT;
        if (modifiersStr.Contains("Shift", StringComparison.OrdinalIgnoreCase))
            modifiers |= NativeMethods.MOD_SHIFT;
        if (modifiersStr.Contains("Win", StringComparison.OrdinalIgnoreCase))
            modifiers |= NativeMethods.MOD_WIN;

        if (modifiers == 0)
            modifiers = NativeMethods.MOD_CONTROL;

        uint key = 0x20; // VK_SPACE

        if (Enum.TryParse<Key>(keyStr, true, out var wpfKey))
        {
            key = (uint)KeyInterop.VirtualKeyFromKey(wpfKey);
        }
        else
        {
            key = keyStr.ToUpperInvariant() switch
            {
                "SPACE" => 0x20,
                "S" => 0x53,
                "Q" => 0x51,
                "E" => 0x45,
                "F1" => 0x70,
                "F2" => 0x71,
                "F3" => 0x72,
                "F4" => 0x73,
                "F8" => 0x77,
                "F9" => 0x78,
                "F10" => 0x79,
                "F11" => 0x7A,
                "F12" => 0x7B,
                _ => 0x20
            };
        }

        return (modifiers, key);
    }
}
