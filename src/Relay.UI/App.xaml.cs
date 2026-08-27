using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Relay.Core.Interfaces;
using Relay.Core.Models;
using Relay.Infrastructure;
using Relay.Infrastructure.Hotkeys;
using Relay.Infrastructure.ScreenCapture;
using Relay.UI.ViewModels;
using Relay.UI.Views;
using Wpf.Ui.Appearance;

namespace Relay.UI;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private IHost? _host;
    private TaskbarIcon? _taskbarIcon;
    private SelectionOverlayWindow? _activeOverlay;
    private FloatingResultWindow? _activeFloatingWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Single Instance Check via Named System Mutex
        bool isNewInstance;
        try
        {
            _singleInstanceMutex = new Mutex(true, @"Global\Relay_SingleInstance_App_Mutex_v1", out isNewInstance);
        }
        catch
        {
            isNewInstance = true;
        }

        if (!isNewInstance)
        {
            bool isBackgroundLaunch = e.Args.Any(arg =>
                arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-minimized", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-tray", StringComparison.OrdinalIgnoreCase));

            if (!isBackgroundLaunch)
            {
                // Try to bring the already-running Relay instance to the foreground
                IntPtr hWnd = NativeMethods.FindWindow(null, "Relay — AI Desktop Assistant");
                if (hWnd != IntPtr.Zero)
                {
                    if (NativeMethods.IsIconic(hWnd))
                        NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
                    else
                        NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOW);

                    NativeMethods.SetForegroundWindow(hWnd);
                }
            }

            Shutdown();
            return;
        }

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
            // 2. Build and start generic host with DI
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

            // Apply WPF-UI dark theme with Mica backdrop and custom indigo accent
            ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.Mica, updateAccent: true);
            ApplicationAccentColorManager.Apply(
                System.Windows.Media.Color.FromRgb(0x63, 0x66, 0xF1),  // Indigo-500
                ApplicationTheme.Dark
            );

            // 3. Load settings and register global hotkey
            var settingsService = _host.Services.GetRequiredService<ISettingsService>();
            await settingsService.LoadSettingsAsync();

            RegisterGlobalHotkeys();

            // 4. Initialize System Tray Icon
            InitializeTrayIcon();

            // 5. Check if starting minimized / in background
            var settings = settingsService.CurrentSettings;
            bool startMinimized = e.Args.Any(arg => arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                                                   arg.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
                                                   arg.Equals("-minimized", StringComparison.OrdinalIgnoreCase) ||
                                                   arg.Equals("-tray", StringComparison.OrdinalIgnoreCase)) ||
                                  settings.StartMinimizedToTray;

            if (startMinimized)
            {
                if (settings.ShowTrayNotifications && _taskbarIcon != null)
                {
                    _taskbarIcon.ShowNotification(
                        "✦ Relay Active",
                        "Relay is running in the background. Press Ctrl + Space or Ctrl + Shift + Space anytime.");
                }

                // Trim working set to minimal footprint when idle in background
                TrimWorkingSet();
            }
            else
            {
                ShowMainWindow();
            }

            // Start periodic background memory trimmer
            StartMemoryTrimmerTimer();
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
                TrimWorkingSet();
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
            _activeFloatingWindow.Closed += (s, e) =>
            {
                _activeFloatingWindow = null;
                TrimWorkingSet();
            };

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

    private MainWindow? _mainWindow;
    private MainWindowViewModel? _mainViewModel;

    private void ShowMainWindow()
    {
        if (_host == null) return;

        if (_mainWindow == null)
        {
            _mainViewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
            _mainViewModel.TriggerCaptureRequested += (s, ev) => Dispatcher.Invoke(() => TriggerScreenSelection(isPromptMode: false));
            _mainViewModel.TriggerPromptCaptureRequested += (s, ev) => Dispatcher.Invoke(() => TriggerScreenSelection(isPromptMode: true));
            _mainViewModel.OpenSettingsRequested += (s, ev) => Dispatcher.Invoke(OpenSettingsWindow);
            _mainViewModel.OpenHistoryRequested += (s, ev) => Dispatcher.Invoke(OpenHistoryWindow);

            _mainWindow = _host.Services.GetRequiredService<MainWindow>();
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
        _mainWindow.Focus();
    }

    private void InitializeTrayIcon()
    {
        try
        {
            System.Drawing.Icon? trayIcon = null;

            // 1. Extract high-res icon directly from running executable process
            try
            {
                string? processPath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(processPath))
                {
                    using var proc = Process.GetCurrentProcess();
                    processPath = proc.MainModule?.FileName;
                }

                if (!string.IsNullOrEmpty(processPath) && System.IO.File.Exists(processPath))
                {
                    trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
                }
            }
            catch { }

            // 2. Load from WPF Pack URI
            if (trayIcon == null)
            {
                string[] packUris = new[]
                {
                    "pack://application:,,,/Relay;component/Assets/relay.ico",
                    "pack://application:,,,/Assets/relay.ico",
                    "pack://application:,,,/Relay.UI;component/Assets/relay.ico"
                };

                foreach (var uriStr in packUris)
                {
                    try
                    {
                        var iconUri = new Uri(uriStr, UriKind.RelativeOrAbsolute);
                        var streamResource = GetResourceStream(iconUri);
                        if (streamResource != null)
                        {
                            using var stream = streamResource.Stream;
                            trayIcon = new System.Drawing.Icon(stream);
                            if (trayIcon != null) break;
                        }
                    }
                    catch { }
                }
            }

            // 3. Fallback to local files
            if (trayIcon == null)
            {
                string[] candidatePaths = new[]
                {
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "relay.ico"),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "relay.ico"),
                    System.IO.Path.Combine(AppContext.BaseDirectory, "relay.ico"),
                    System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "relay.ico")
                };

                foreach (var path in candidatePaths)
                {
                    if (System.IO.File.Exists(path))
                    {
                        try
                        {
                            trayIcon = new System.Drawing.Icon(path);
                            if (trayIcon != null) break;
                        }
                        catch { }
                    }
                }
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

    private System.Windows.Threading.DispatcherTimer? _memoryTimer;

    private void StartMemoryTrimmerTimer()
    {
        _memoryTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _memoryTimer.Tick += (s, e) =>
        {
            // Only trim when no visible application windows are actively open
            bool hasOpenWindows = Windows.Cast<Window>().Any(w => w.IsVisible && !(w is SelectionOverlayWindow));
            if (!hasOpenWindows)
            {
                TrimWorkingSet();
            }
        };
        _memoryTimer.Start();
    }

    /// <summary>
    /// Minimizes process working set memory footprint down to ~8-12 MB when idle in background.
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            using var currentProcess = Process.GetCurrentProcess();
            NativeMethods.SetProcessWorkingSetSize(currentProcess.Handle, (IntPtr)(-1), (IntPtr)(-1));
            NativeMethods.EmptyWorkingSet(currentProcess.Handle);
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

            if (_singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch { }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
        }
        catch { }

        base.OnExit(e);
    }
}
