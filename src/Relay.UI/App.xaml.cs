using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Relay.Core.Interfaces;
using Relay.Core.Models;
using Relay.Infrastructure;
using Relay.Infrastructure.Hotkeys;
using Relay.UI.ViewModels;
using Relay.UI.Views;

namespace Relay.UI;

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
                    services.AddRelayInfrastructure();

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

            RegisterGlobalHotkeys();

            // 3. Initialize System Tray Icon
            InitializeTrayIcon();

            // 4. Check if starting minimized / in background
            var settings = settingsService.CurrentSettings;
            bool startMinimized = e.Args.Any(arg => arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                                                   arg.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
                                                   arg.Equals("-minimized", StringComparison.OrdinalIgnoreCase) ||
                                                   arg.Equals("-tray", StringComparison.OrdinalIgnoreCase)) ||
                                  settings.StartMinimizedToTray;

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var mainVm = _host.Services.GetRequiredService<MainWindowViewModel>();
            mainVm.TriggerCaptureRequested += (s, ev) => Dispatcher.Invoke(() => TriggerScreenSelection(isPromptMode: false));
            mainVm.TriggerPromptCaptureRequested += (s, ev) => Dispatcher.Invoke(() => TriggerScreenSelection(isPromptMode: true));
            mainVm.OpenSettingsRequested += (s, ev) => Dispatcher.Invoke(OpenSettingsWindow);
            mainVm.OpenHistoryRequested += (s, ev) => Dispatcher.Invoke(OpenHistoryWindow);

            if (startMinimized)
            {
                if (settings.ShowTrayNotifications && _taskbarIcon != null)
                {
                    _taskbarIcon.ShowNotification(
                        "✦ Relay Active",
                        "Relay is running in the background. Press Ctrl + Space or Ctrl + Shift + Space anytime.");
                }
            }
            else
            {
                mainWindow.Show();
                mainWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Initialization failed: {ex.Message}", "Relay Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RegisterGlobalHotkeys()
    {
        if (_host == null) return;

        try
        {
            var settingsService = _host.Services.GetRequiredService<ISettingsService>();
            var hotkeyService = _host.Services.GetRequiredService<IHotkeyService>();

            var settings = settingsService.CurrentSettings;
            var (mod1, key1) = HotkeyParser.Parse(settings.HotkeyModifiers, settings.HotkeyKey);
            var (mod2, key2) = HotkeyParser.Parse(settings.PromptHotkeyModifiers, settings.PromptHotkeyKey);

            hotkeyService.UnregisterAll();
            hotkeyService.RegisterHotkey(mod1, key1, id: 9001); // Quick capture
            hotkeyService.RegisterHotkey(mod2, key2, id: 9002); // Prompt capture

            hotkeyService.HotkeyPressed -= OnHotkeyPressed;
            hotkeyService.HotkeyPressed += OnHotkeyPressed;
        }
        catch { }
    }

    private void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs args)
    {
        Dispatcher.BeginInvoke(new Action(() => TriggerScreenSelection(isPromptMode: args.IsPromptMode)));
    }

    public void TriggerScreenSelection(bool isPromptMode = false)
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
            overlayVm.IsPromptMode = isPromptMode;
            overlayVm.UserPrompt = string.Empty;
            _activeOverlay = new SelectionOverlayWindow(overlayVm);

            overlayVm.SelectionCompleted += async (s, result) =>
            {
                _activeOverlay = null;
                await OpenFloatingResultAsync(result.Region, result.Context, result.Prompt);
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

    private async Task OpenFloatingResultAsync(CaptureRegion region, ScreenContext context, string? prompt = null)
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

            await resultVm.InitializeWithCaptureAsync(region, context, prompt);
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

            // Re-register hotkeys in case settings changed
            RegisterGlobalHotkeys();

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
            historyWin.Activate();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open history: {ex.Message}", "Relay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowMainWindow()
    {
        if (_host == null) return;

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }

        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        mainWindow.Activate();
        mainWindow.Focus();
    }

    private void InitializeTrayIcon()
    {
        try
        {
            System.Drawing.Icon? trayIcon = null;

            // Load from pack URI stream
            try
            {
                var iconUri = new Uri("pack://application:,,,/Relay.UI;component/Assets/relay.ico", UriKind.RelativeOrAbsolute);
                var streamResource = GetResourceStream(iconUri);
                if (streamResource != null)
                {
                    using var stream = streamResource.Stream;
                    trayIcon = new System.Drawing.Icon(stream);
                }
            }
            catch { }

            // Fallback to local file if needed
            if (trayIcon == null)
            {
                try
                {
                    string icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "relay.ico");
                    if (System.IO.File.Exists(icoPath))
                    {
                        trayIcon = new System.Drawing.Icon(icoPath);
                    }
                }
                catch { }
            }

            trayIcon ??= SystemIcons.Application;

            _taskbarIcon = new TaskbarIcon
            {
                ToolTipText = "Relay — AI Screen Assistant (Ctrl + Space / Ctrl + Shift + Space)",
                Icon = trayIcon
            };

            // Force notify icon registration with Windows notification area
            _taskbarIcon.ForceCreate(true);

            var contextMenu = new ContextMenu();

            var openDashboardItem = new MenuItem
            {
                Header = "✦ Open Relay Dashboard",
                FontWeight = FontWeights.Bold
            };
            openDashboardItem.Click += (s, e) => ShowMainWindow();

            var captureItem = new MenuItem
            {
                Header = "📷 Quick Capture (Ctrl + Space)"
            };
            captureItem.Click += (s, e) => TriggerScreenSelection(isPromptMode: false);

            var promptCaptureItem = new MenuItem
            {
                Header = "💬 Ask AI with Prompt (Ctrl + Shift + Space)"
            };
            promptCaptureItem.Click += (s, e) => TriggerScreenSelection(isPromptMode: true);

            var historyItem = new MenuItem
            {
                Header = "🕒 Analysis History"
            };
            historyItem.Click += (s, e) => OpenHistoryWindow();

            var settingsItem = new MenuItem
            {
                Header = "⚙ Settings & API Key"
            };
            settingsItem.Click += (s, e) => OpenSettingsWindow();

            var startupService = _host?.Services.GetService<IStartupService>();
            var runOnStartupItem = new MenuItem
            {
                Header = "🚀 Run on Windows Startup",
                IsCheckable = true,
                IsChecked = startupService?.IsStartupEnabled() ?? false
            };
            runOnStartupItem.Click += async (s, e) =>
            {
                if (startupService != null && _host != null)
                {
                    bool newState = runOnStartupItem.IsChecked;
                    startupService.SetStartup(newState, startMinimized: true);
                    var settingsService = _host.Services.GetRequiredService<ISettingsService>();
                    settingsService.CurrentSettings.StartWithWindows = newState;
                    await settingsService.SaveSettingsAsync(settingsService.CurrentSettings);
                }
            };

            var separator1 = new Separator();
            var separator2 = new Separator();

            var exitItem = new MenuItem
            {
                Header = "✕ Exit Relay"
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
            contextMenu.Items.Add(promptCaptureItem);
            contextMenu.Items.Add(historyItem);
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(separator1);
            contextMenu.Items.Add(runOnStartupItem);
            contextMenu.Items.Add(separator2);
            contextMenu.Items.Add(exitItem);

            _taskbarIcon.ContextMenu = contextMenu;

            // Single click and double click both restore/focus dashboard
            _taskbarIcon.TrayLeftMouseDown += (s, e) => ShowMainWindow();
            _taskbarIcon.TrayMouseDoubleClick += (s, e) => ShowMainWindow();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrayIcon] Error initializing tray icon: {ex.Message}");
        }
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
