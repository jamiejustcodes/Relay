using System.ComponentModel;
using System.Windows;
using Relay.UI.ViewModels;

namespace Relay.UI.Views;

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
        App.TrimWorkingSet();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Hide to tray rather than exiting whole process
        e.Cancel = true;
        Hide();
        App.TrimWorkingSet();
    }
}
