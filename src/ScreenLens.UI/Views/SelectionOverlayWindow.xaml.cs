using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenLens.UI.ViewModels;

namespace ScreenLens.UI.Views;

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
        Canvas.SetLeft(InstructionBar, (Width - 480) / 2);
        Canvas.SetTop(InstructionBar, Height - 80);

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
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDragging = true;
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
                Hide();
                await _viewModel.ConfirmSelectionAsync(_dpiScale);
                Close();
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

    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.CancelSelection();
            Close();
        }
        else if (e.Key == Key.Space)
        {
            Hide();
            await _viewModel.SelectEntireScreenAsync(_dpiScale);
            Close();
        }
        else if (e.Key == Key.Enter && _viewModel.SelectionWidth > 10 && _viewModel.SelectionHeight > 10)
        {
            Hide();
            await _viewModel.ConfirmSelectionAsync(_dpiScale);
            Close();
        }
    }
}
