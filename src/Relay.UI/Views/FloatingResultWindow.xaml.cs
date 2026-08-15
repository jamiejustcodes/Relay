using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Relay.Core.Models;
using Relay.UI.ViewModels;

namespace Relay.UI.Views;

public partial class FloatingResultWindow : Window
{
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private readonly FloatingResultViewModel _viewModel;

    public FloatingResultWindow(FloatingResultViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.CloseRequested += (s, e) => Close();

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FloatingResultViewModel.IsPinned))
            {
                ApplyTopmostState(_viewModel.IsPinned);
            }
        };

        Loaded += (s, e) => ApplyTopmostState(_viewModel.IsPinned);
    }

    private void ApplyTopmostState(bool isPinned)
    {
        Topmost = isPinned;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, isPinned ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    public void PositionNearSelection(CaptureRegion region, double dpiScale = 1.0)
    {
        double screenLeft = SystemParameters.VirtualScreenLeft;
        double screenTop = SystemParameters.VirtualScreenTop;
        double screenWidth = SystemParameters.VirtualScreenWidth;
        double screenHeight = SystemParameters.VirtualScreenHeight;

        double selLeftDips = region.X / dpiScale;
        double selTopDips = region.Y / dpiScale;
        double selWidthDips = region.Width / dpiScale;
        double selHeightDips = region.Height / dpiScale;

        // Try positioning to the right of the selection
        double targetLeft = selLeftDips + selWidthDips + 20;
        double targetTop = selTopDips;

        // If overflowing right edge, place to the left or below
        if (targetLeft + Width > screenLeft + screenWidth)
        {
            targetLeft = Math.Max(screenLeft + 20, selLeftDips - Width - 20);
        }

        // Clamp vertically
        if (targetTop + 450 > screenTop + screenHeight)
        {
            targetTop = Math.Max(screenTop + 20, screenTop + screenHeight - 500);
        }

        if (targetLeft < screenLeft) targetLeft = screenLeft + 40;
        if (targetTop < screenTop) targetTop = screenTop + 40;

        Left = targetLeft;
        Top = targetTop;
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.SubmitQuestionCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
