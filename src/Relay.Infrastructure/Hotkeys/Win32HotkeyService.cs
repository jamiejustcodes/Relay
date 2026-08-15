using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Relay.Core.Interfaces;
using Relay.Infrastructure.ScreenCapture;

namespace Relay.Infrastructure.Hotkeys;

public class Win32HotkeyService : IHotkeyService
{
    private class HotkeyRegistration
    {
        public int Id { get; set; }
        public uint Modifiers { get; set; }
        public uint Key { get; set; }
    }

    private readonly Dictionary<int, HotkeyRegistration> _registeredHotkeys = new();
    private readonly HashSet<int> _registeredWin32Ids = new();
    private HwndSource? _hwndSource;
    private IntPtr _hookHandle = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private long _lastTriggerTick = 0;
    private int _lastTriggerId = 0;
    private bool _disposed;

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public bool IsRegistered => _registeredHotkeys.Count > 0;

    public Win32HotkeyService()
    {
        EnsureMessageWindow();
        InstallLowLevelHook();
    }

    private void EnsureMessageWindow()
    {
        if (_hwndSource == null)
        {
            try
            {
                var parameters = new HwndSourceParameters("RelayHotkeyListener")
                {
                    WindowStyle = unchecked((int)0x80000000), // WS_POPUP
                    ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW (prevents taskbar item)
                    ParentWindow = IntPtr.Zero, // Top-level desktop window
                    Width = 0,
                    Height = 0,
                    PositionX = 0,
                    PositionY = 0
                };

                _hwndSource = new HwndSource(parameters);
                _hwndSource.AddHook(HwndHook);
            }
            catch
            {
                // Fallback: LowLevelKeyboardHook will still operate
            }
        }
    }

    private void InstallLowLevelHook()
    {
        if (_hookHandle != IntPtr.Zero) return;

        try
        {
            _hookProc = LowLevelKeyboardHookCallback;
            IntPtr hMod = NativeMethods.GetModuleHandle(null);
            _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookProc, hMod, 0);
        }
        catch
        {
            // Ignore hook installation errors
        }
    }

    public bool RegisterHotkey(uint modifiers, uint key, int id = 9001)
    {
        EnsureMessageWindow();
        InstallLowLevelHook();

        UnregisterHotkey(id);

        _registeredHotkeys[id] = new HotkeyRegistration
        {
            Id = id,
            Modifiers = modifiers,
            Key = key
        };

        bool win32Registered = false;
        if (_hwndSource != null && _hwndSource.Handle != IntPtr.Zero)
        {
            uint fullModifiers = modifiers | NativeMethods.MOD_NOREPEAT;
            win32Registered = NativeMethods.RegisterHotKey(_hwndSource.Handle, id, fullModifiers, key);
            if (win32Registered)
            {
                _registeredWin32Ids.Add(id);
            }
        }

        // Return true if registered via Win32 or protected by the low-level hook
        return win32Registered || _hookHandle != IntPtr.Zero;
    }

    public void UnregisterHotkey(int id = 9001)
    {
        _registeredHotkeys.Remove(id);

        if (_registeredWin32Ids.Contains(id) && _hwndSource != null && _hwndSource.Handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, id);
            _registeredWin32Ids.Remove(id);
        }
    }

    public void UnregisterAll()
    {
        _registeredHotkeys.Clear();

        if (_hwndSource != null && _hwndSource.Handle != IntPtr.Zero)
        {
            foreach (var id in _registeredWin32Ids.ToList())
            {
                NativeMethods.UnregisterHotKey(_hwndSource.Handle, id);
            }
            _registeredWin32Ids.Clear();
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_registeredHotkeys.ContainsKey(id))
            {
                handled = true;
                TriggerHotkey(id);
            }
        }

        return IntPtr.Zero;
    }

    private IntPtr LowLevelKeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            var kbd = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            uint vk = kbd.vkCode;

            // Read global modifier key states
            bool ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
            bool shift = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
            bool alt = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
            bool win = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0 ||
                       (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0;

            uint currentMods = 0;
            if (ctrl) currentMods |= NativeMethods.MOD_CONTROL;
            if (shift) currentMods |= NativeMethods.MOD_SHIFT;
            if (alt) currentMods |= NativeMethods.MOD_ALT;
            if (win) currentMods |= NativeMethods.MOD_WIN;

            foreach (var reg in _registeredHotkeys.Values)
            {
                uint expectedMods = reg.Modifiers & ~NativeMethods.MOD_NOREPEAT;
                if (reg.Key == vk && expectedMods == currentMods)
                {
                    TriggerHotkey(reg.Id);
                    break;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void TriggerHotkey(int id)
    {
        long now = Environment.TickCount64;
        // Debounce within 350ms to prevent duplicate triggers
        if (id == _lastTriggerId && (now - _lastTriggerTick) < 350)
        {
            return;
        }

        _lastTriggerTick = now;
        _lastTriggerId = id;

        bool isPromptMode = (id == 9002);
        HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs { HotkeyId = id, IsPromptMode = isPromptMode });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnregisterAll();

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _hookProc = null;
        }

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(HwndHook);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
    }
}
