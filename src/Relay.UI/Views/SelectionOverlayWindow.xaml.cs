using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Relay.UI.ViewModels;

namespace Relay.UI.Views;

public partial class SelectionOverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private Point _dragStartPoint;
    private bool _isDragging;
    private double _dpiScale = 1.0;

    public SelectionOverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Fit window to the entire virtual desktop spanning all monitors
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        ScreenBoundsGeometry.Rect = new Rect(0, 0, Width, Height);
        SelectionHoleGeometry.Rect = Rect.Empty;

        // Position instruction bar at bottom center
        Canvas.SetLeft(InstructionBar, (Width - 520) / 2);
        Canvas.SetTop(InstructionBar, Height - 80);

        if (_viewModel.IsPromptMode)
        {
            InstructionText.Text = "Prompt Mode: Drag region, type prompt & press Enter";
        }
        else
        {
            InstructionText.Text = "Drag region to analyze";
        }

        var presentationSource = PresentationSource.FromVisual(this);
        if (presentationSource?.CompositionTarget != null)
        {
            _dpiScale = presentationSource.CompositionTarget.TransformToDevice.M11;
        }

        Activate();
        Focus();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        // If clicking inside PromptBar or InstructionBar, let child controls handle it
        if (e.OriginalSource is DependencyObject dep)
        {
            if (IsDescendantOf(dep, PromptBar) || IsDescendantOf(dep, InstructionBar))
            {
                return;
            }
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDragging = true;
            _viewModel.IsPromptBarVisible = false;
            Cursor = Cursors.Cross;
            _dragStartPoint = e.GetPosition(this);
            _viewModel.StartSelection(_dragStartPoint);
            CaptureMouse();
            UpdateHoleGeometry();
        }
        else if (e.RightButton == MouseButtonState.Pressed)
        {
            _viewModel.CancelSelection();
            Close();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            Point currentPoint = e.GetPosition(this);
            _viewModel.UpdateSelection(_dragStartPoint, currentPoint);
            UpdateHoleGeometry();
        }
    }

    private async void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();

            if (_viewModel.SelectionWidth > 15 && _viewModel.SelectionHeight > 15)
            {
                if (_viewModel.IsPromptMode)
                {
                    // Show Prompt Input bar right below/above the selection box
                    _viewModel.CalculatePromptBarPosition(Width, Height);
                    Cursor = Cursors.Arrow;
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        PromptTextBox.Focus();
                        Keyboard.Focus(PromptTextBox);
                        PromptTextBox.SelectAll();
                    }, System.Windows.Threading.DispatcherPriority.Input);
                }
                else
                {
                    Hide();
                    await _viewModel.ConfirmSelectionAsync(_dpiScale);
                    Close();
                }
            }
            else
            {
                _viewModel.CancelSelection();
                Close();
            }
        }
    }

    private void UpdateHoleGeometry()
    {
        if (_viewModel.SelectionWidth > 0 && _viewModel.SelectionHeight > 0)
        {
            SelectionHoleGeometry.Rect = new Rect(
                _viewModel.SelectionLeft,
                _viewModel.SelectionTop,
                _viewModel.SelectionWidth,
                _viewModel.SelectionHeight
            );
        }
        else
        {
            SelectionHoleGeometry.Rect = Rect.Empty;
        }
    }

    private async void OnPromptKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SubmitSelectionWithPromptAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _viewModel.CancelSelection();
            Close();
        }
    }

    private async void OnSendPromptClick(object sender, RoutedEventArgs e)
    {
        await SubmitSelectionWithPromptAsync();
    }

    private async Task SubmitSelectionWithPromptAsync()
    {
        string prompt = PromptTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = _viewModel.UserPrompt?.Trim() ?? string.Empty;
        }
        Hide();
        await _viewModel.ConfirmSelectionAsync(_dpiScale, prompt);
        Close();
    }

    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.CancelSelection();
            Close();
        }
        else if (e.Key == Key.Space && !_viewModel.IsPromptBarVisible)
        {
            Hide();
            await _viewModel.SelectEntireScreenAsync(_dpiScale);
            Close();
        }
        else if (e.Key == Key.Tab && !_viewModel.IsPromptBarVisible && _viewModel.SelectionWidth > 15 && _viewModel.SelectionHeight > 15)
        {
            // Switch to Prompt Bar mode dynamically
            _viewModel.IsPromptMode = true;
            _viewModel.CalculatePromptBarPosition(Width, Height);
            Cursor = Cursors.Arrow;
            PromptTextBox.Focus();
            Keyboard.Focus(PromptTextBox);
        }
        else if (e.Key == Key.Enter && !_viewModel.IsPromptBarVisible && _viewModel.SelectionWidth > 10 && _viewModel.SelectionHeight > 10)
        {
            Hide();
            await _viewModel.ConfirmSelectionAsync(_dpiScale);
            Close();
        }
    }

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject? parent)
    {
        if (parent == null || child == null) return false;
        var current = child;
        while (current != null)
        {
            if (current == parent) return true;
            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
            {
                current = VisualTreeHelper.GetParent(current);
            }
            else
            {
                current = LogicalTreeHelper.GetParent(current);
            }
        }
        return false;
    }
}
