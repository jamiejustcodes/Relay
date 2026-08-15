using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ScreenLens.Core.Interfaces;
using ScreenLens.Core.Models;
using ScreenLens.Infrastructure;
using ScreenLens.Infrastructure.Hotkeys;
using ScreenLens.UI.ViewModels;
using ScreenLens.UI.Views;

namespace ScreenLens.UI;

public partial class App : Application
{
    private IHost? _host;
    private TaskbarIcon? _taskbarIcon;
    private SelectionOverlayWindow? _activeOverlay;
    private FloatingResultWindow? _activeFloatingWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global Exception Handlers to prevent unexpected process crashes
        DispatcherUnhandledException += (sender, args) =>
        {
            args.Handled = true;
            MessageBox.Show(
                $"Relay encountered an issue:\n\n{args.Exception.Message}",
                "Relay Notice",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"Relay unhandled error:\n\n{ex.Message}",
                    "Relay Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            // 1. Build and start generic host with DI
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Infrastructure services
                    services.AddScreenLensInfrastructure();

                    // ViewModels
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddTransient<OverlayViewModel>();
                    services.AddTransient<FloatingResultViewModel>();
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<HistoryViewModel>();

                    // Views
                    services.AddSingleton<MainWindow>();
                    services.AddTransient<SelectionOverlayWindow>();
                    services.AddTransient<FloatingResultWindow>();
                    services.AddTransient<SettingsWindow>();
                    services.AddTransient<HistoryWindow>();
                })
                .Build();

            await _host.StartAsync();

            // 2. Load settings and register global hotkey
            var settingsService = _host.Services.GetRequiredService<ISettingsService>();
            await settingsService.LoadSettingsAsync();

            RegisterGlobalHotkey();

            // 3. Initialize System Tray Icon
            InitializeTrayIcon();

            // 4. Open Main Dashboard
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var mainVm = _host.Services.GetRequiredService<MainWindowViewModel>();
            mainVm.TriggerCaptureRequested += (s, ev) => Dispatcher.Invoke(TriggerScreenSelection);
            mainVm.OpenSettingsRequested += (s, ev) => Dispatcher.Invoke(OpenSettingsWindow);
            mainVm.OpenHistoryRequested += (s, ev) => Dispatcher.Invoke(OpenHistoryWindow);

            mainWindow.Show();
            mainWindow.Activate();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Initialization failed: {ex.Message}", "Relay Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RegisterGlobalHotkey()
    {
        if (_host == null) return;

        try
        {
            var settingsService = _host.Services.GetRequiredService<ISettingsService>();
            var hotkeyService = _host.Services.GetRequiredService<IHotkeyService>();

            var settings = settingsService.CurrentSettings;
            var (modifiers, key) = HotkeyParser.Parse(settings.HotkeyModifiers, settings.HotkeyKey);

            hotkeyService.RegisterHotkey(modifiers, key);
            hotkeyService.HotkeyPressed += (s, args) =>
            {
                Dispatcher.Invoke(TriggerScreenSelection);
            };
        }
        catch { }
    }

    public void TriggerScreenSelection()
    {
        if (_host == null) return;

        try
        {
            // If an overlay is already active, ignore
            if (_activeOverlay != null && _activeOverlay.IsVisible)
                return;

            var windowContextService = _host.Services.GetRequiredService<IWindowContextService>();
            var currentContext = windowContextService.GetForegroundWindowContext();

            // Privacy Check: if foreground app is excluded, don't capture silently
            if (currentContext.IsExcludedApplication)
            {
                MessageBox.Show(
                    $"Relay capture was prevented because '{currentContext.ApplicationName}' is in your privacy exclusion list.",
                    "Relay Privacy Guard",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var overlayVm = _host.Services.GetRequiredService<OverlayViewModel>();
            _activeOverlay = new SelectionOverlayWindow(overlayVm);

            overlayVm.SelectionCompleted += async (s, result) =>
            {
                _activeOverlay = null;
                await OpenFloatingResultAsync(result.Region, result.Context);
            };

            overlayVm.SelectionCancelled += (s, e) =>
            {
                _activeOverlay = null;
            };

            _activeOverlay.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open selection overlay: {ex.Message}", "Relay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task OpenFloatingResultAsync(CaptureRegion region, ScreenContext context)
    {
        if (_host == null) return;

        try
        {
            // Close previous result window if open
            _activeFloatingWindow?.Close();

            var resultVm = _host.Services.GetRequiredService<FloatingResultViewModel>();
            _activeFloatingWindow = new FloatingResultWindow(resultVm);

            // Position floating result window near the crop area
            _activeFloatingWindow.PositionNearSelection(region, region.DpiScale);
            _activeFloatingWindow.Show();

            await resultVm.InitializeWithCaptureAsync(region, context);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not display results: {ex.Message}", "Relay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void OpenSettingsWindow()
    {
        if (_host == null) return;

        try
        {
            var settingsVm = _host.Services.GetRequiredService<SettingsViewModel>();
            var settingsWin = new SettingsWindow(settingsVm);
            settingsWin.ShowDialog();

            // Re-register hotkey in case settings changed
            RegisterGlobalHotkey();

            // Refresh Dashboard
            var mainVm = _host.Services.GetService<MainWindowViewModel>();
            _ = mainVm?.RefreshStateAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open settings: {ex.Message}", "Relay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void OpenHistoryWindow()
    {
        if (_host == null) return;

        try
        {
            var historyVm = _host.Services.GetRequiredService<HistoryViewModel>();
            var historyWin = new HistoryWindow(historyVm);
            historyWin.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open history: {ex.Message}", "Relay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void InitializeTrayIcon()
    {
        try
        {
            _taskbarIcon = new TaskbarIcon
            {
                ToolTipText = "Relay — AI Desktop Assistant (Ctrl + Space)",
                Icon = SystemIcons.Application
            };

            var contextMenu = new ContextMenu();

            var openDashboardItem = new MenuItem
            {
                Header = "✦ Open Relay Dashboard",
                FontWeight = FontWeights.Bold
            };
            openDashboardItem.Click += (s, e) =>
            {
                if (_host != null)
                {
                    var main = _host.Services.GetRequiredService<MainWindow>();
                    main.Show();
                    main.Activate();
                }
            };

            var captureItem = new MenuItem
            {
                Header = "📷 Capture Screen (Ctrl + Space)"
            };
            captureItem.Click += (s, e) => TriggerScreenSelection();

            var historyItem = new MenuItem
            {
                Header = "🕒 Analysis History"
            };
            historyItem.Click += (s, e) => OpenHistoryWindow();

            var settingsItem = new MenuItem
            {
                Header = "⚙ Settings"
            };
            settingsItem.Click += (s, e) => OpenSettingsWindow();

            var separator = new Separator();

            var exitItem = new MenuItem
            {
                Header = "Exit Relay"
            };
            exitItem.Click += (s, e) =>
            {
                if (_taskbarIcon != null)
                {
                    _taskbarIcon.Dispose();
                    _taskbarIcon = null;
                }
                Current.Shutdown();
            };

            contextMenu.Items.Add(openDashboardItem);
            contextMenu.Items.Add(captureItem);
            contextMenu.Items.Add(historyItem);
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(separator);
            contextMenu.Items.Add(exitItem);

            _taskbarIcon.ContextMenu = contextMenu;
            _taskbarIcon.TrayMouseDoubleClick += (s, e) =>
            {
                if (_host != null)
                {
                    var main = _host.Services.GetRequiredService<MainWindow>();
                    main.Show();
                    main.Activate();
                }
            };
        }
        catch { }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_taskbarIcon != null)
            {
                _taskbarIcon.Dispose();
                _taskbarIcon = null;
            }

            if (_host != null)
            {
                var hotkeyService = _host.Services.GetService<IHotkeyService>();
                hotkeyService?.Dispose();

                await _host.StopAsync();
                _host.Dispose();
            }
        }
        catch { }

        base.OnExit(e);
    }
}
