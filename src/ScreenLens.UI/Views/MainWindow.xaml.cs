using System.ComponentModel;
using System.Windows;
using ScreenLens.UI.ViewModels;

namespace ScreenLens.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += async (s, e) => await _viewModel.RefreshStateAsync();
        Activated += async (s, e) => await _viewModel.RefreshStateAsync();
    }

    private void OnMinimizeToTrayClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Hide to tray rather than exiting whole process
        e.Cancel = true;
        Hide();
    }
}
