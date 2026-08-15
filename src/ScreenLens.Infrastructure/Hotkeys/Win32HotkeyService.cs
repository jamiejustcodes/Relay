using System.Runtime.InteropServices;
using System.Windows.Interop;
using ScreenLens.Core.Interfaces;
using ScreenLens.Infrastructure.ScreenCapture;

namespace ScreenLens.Infrastructure.Hotkeys;

public class Win32HotkeyService : IHotkeyService
{
    private const int HotkeyId = 9001;
    private HwndSource? _hwndSource;
    private bool _isRegistered;
    private bool _disposed;

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public bool IsRegistered => _isRegistered;

    public Win32HotkeyService()
    {
        EnsureMessageWindow();
    }

    private void EnsureMessageWindow()
    {
        if (_hwndSource == null)
        {
            var parameters = new HwndSourceParameters("ScreenLensHotkeyListener")
            {
                WindowStyle = 0,
                ExtendedWindowStyle = 0,
                ParentWindow = new IntPtr(-3) // HWND_MESSAGE
            };

            _hwndSource = new HwndSource(parameters);
            _hwndSource.AddHook(HwndHook);
        }
    }

    public bool RegisterHotkey(uint modifiers, uint key)
    {
        EnsureMessageWindow();
        if (_hwndSource == null) return false;

        UnregisterHotkey();

        // NativeMethods.MOD_NOREPEAT prevents multiple triggers when held down
        uint fullModifiers = modifiers | NativeMethods.MOD_NOREPEAT;

        bool result = NativeMethods.RegisterHotKey(_hwndSource.Handle, HotkeyId, fullModifiers, key);
        _isRegistered = result;
        return result;
    }

    public void UnregisterHotkey()
    {
        if (_isRegistered && _hwndSource != null && _hwndSource.Handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, HotkeyId);
            _isRegistered = false;
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs());
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnregisterHotkey();

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(HwndHook);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
    }
}
