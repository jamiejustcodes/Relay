using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenLens.Core.Interfaces;
using ScreenLens.Core.Models;

namespace ScreenLens.UI.ViewModels;

public partial class OverlayViewModel : ObservableObject
{
    private readonly IScreenCaptureService _captureService;
    private readonly IWindowContextService _windowContextService;
    private readonly IOcrService _ocrService;

    [ObservableProperty]
    private bool _isSelecting;

    [ObservableProperty]
    private double _selectionLeft;

    [ObservableProperty]
    private double _selectionTop;

    [ObservableProperty]
    private double _selectionWidth;

    [ObservableProperty]
    private double _selectionHeight;

    [ObservableProperty]
    private string _dimensionText = string.Empty;

    public event EventHandler<(CaptureRegion Region, ScreenContext Context)>? SelectionCompleted;
    public event EventHandler? SelectionCancelled;

    public OverlayViewModel(
        IScreenCaptureService captureService,
        IWindowContextService windowContextService,
        IOcrService ocrService)
    {
        _captureService = captureService;
        _windowContextService = windowContextService;
        _ocrService = ocrService;
    }

    public void StartSelection(Point startPoint)
    {
        IsSelecting = true;
        SelectionLeft = startPoint.X;
        SelectionTop = startPoint.Y;
        SelectionWidth = 0;
        SelectionHeight = 0;
        UpdateDimensionText();
    }

    public void UpdateSelection(Point startPoint, Point currentPoint)
    {
        double left = Math.Min(startPoint.X, currentPoint.X);
        double top = Math.Min(startPoint.Y, currentPoint.Y);
        double width = Math.Abs(currentPoint.X - startPoint.X);
        double height = Math.Abs(currentPoint.Y - startPoint.Y);

        SelectionLeft = left;
        SelectionTop = top;
        SelectionWidth = width;
        SelectionHeight = height;
        UpdateDimensionText();
    }

    private void UpdateDimensionText()
    {
        DimensionText = $"{(int)SelectionWidth} × {(int)SelectionHeight} px";
    }

    public async Task ConfirmSelectionAsync(double dpiScale = 1.0)
    {
        if (SelectionWidth < 10 || SelectionHeight < 10)
        {
            CancelSelection();
            return;
        }

        // 1. Capture foreground window context
        var context = _windowContextService.GetForegroundWindowContext();

        // 2. Physical device pixel conversion
        int physicalX = (int)Math.Round(SelectionLeft * dpiScale);
        int physicalY = (int)Math.Round(SelectionTop * dpiScale);
        int physicalW = (int)Math.Round(SelectionWidth * dpiScale);
        int physicalH = (int)Math.Round(SelectionHeight * dpiScale);

        var region = await _captureService.CaptureRegionAsync(physicalX, physicalY, physicalW, physicalH, dpiScale);

        // 3. Fast offline OCR extraction
        string ocrText = await _ocrService.RecognizeTextAsync(region.ImageBytes);
        if (!string.IsNullOrWhiteSpace(ocrText))
        {
            context = context with { LocalOcrText = ocrText };
        }

        IsSelecting = false;
        SelectionCompleted?.Invoke(this, (region, context));
    }

    public async Task SelectEntireScreenAsync(double dpiScale = 1.0)
    {
        var context = _windowContextService.GetForegroundWindowContext();
        var region = await _captureService.CaptureVirtualScreenAsync();

        string ocrText = await _ocrService.RecognizeTextAsync(region.ImageBytes);
        if (!string.IsNullOrWhiteSpace(ocrText))
        {
            context = context with { LocalOcrText = ocrText };
        }

        IsSelecting = false;
        SelectionCompleted?.Invoke(this, (region, context));
    }

    public void CancelSelection()
    {
        IsSelecting = false;
        SelectionCancelled?.Invoke(this, EventArgs.Empty);
    }
}
