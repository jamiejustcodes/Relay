using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Relay.Core.Interfaces;
using Relay.Core.Models;
using Relay.Infrastructure.Ai;
using Relay.Infrastructure.Hotkeys;

namespace Relay.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IAiProviderFactory _aiProviderFactory;
    private readonly IHotkeyService _hotkeyService;
    private readonly IStartupService _startupService;
    private readonly IOllamaManagementService _ollamaService;

    // ── Provider Toggle ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeminiSelected))]
    [NotifyPropertyChangedFor(nameof(IsOllamaSelected))]
    private string _activeProvider = "gemini";

    public bool IsGeminiSelected => ActiveProvider.Equals("gemini", StringComparison.OrdinalIgnoreCase);
    public bool IsOllamaSelected => ActiveProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase);

    // ── Gemini Settings ──

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private bool _showApiKey;

    [ObservableProperty]
    private string _selectedModel = "gemini-flash-latest";

    [ObservableProperty]
    private ObservableCollection<string> _availableModels = new();

    // ── Ollama Settings ──

    [ObservableProperty]
    private bool _isOllamaInstalled;

    [ObservableProperty]
    private bool _isOllamaRunning;

    [ObservableProperty]
    private string _ollamaStatusText = "Checking...";

    [ObservableProperty]
    private string _ollamaBaseUrl = "http://localhost:11434";

    [ObservableProperty]
    private string _selectedOllamaModel = "llava";

    [ObservableProperty]
    private bool _isSelectedOllamaModelInstalled;

    [ObservableProperty]
    private string _selectedModelStatusText = "Checking...";

    private readonly HashSet<string> _installedVisionModels = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private ObservableCollection<string> _ollamaModels = new();

    [ObservableProperty]
    private bool _isInstallingOllama;

    [ObservableProperty]
    private double _ollamaInstallProgress;

    [ObservableProperty]
    private bool _isPullingModel;

    [ObservableProperty]
    private double _ollamaPullProgress;

    [ObservableProperty]
    private string _ollamaPullStatus = string.Empty;

    partial void OnSelectedOllamaModelChanged(string value)
    {
        UpdateSelectedModelInstallationStatus();
    }

    public void UpdateSelectedModelInstallationStatus()
    {
        if (string.IsNullOrWhiteSpace(SelectedOllamaModel))
        {
            IsSelectedOllamaModelInstalled = false;
            SelectedModelStatusText = "No model selected";
            return;
        }

        bool isInstalled = _installedVisionModels.Any(m =>
            m.Equals(SelectedOllamaModel, StringComparison.OrdinalIgnoreCase) ||
            m.StartsWith(SelectedOllamaModel + ":", StringComparison.OrdinalIgnoreCase) ||
            SelectedOllamaModel.StartsWith(m + ":", StringComparison.OrdinalIgnoreCase));

        IsSelectedOllamaModelInstalled = isInstalled;
        SelectedModelStatusText = isInstalled
            ? "● Installed & Ready"
            : "○ Not Downloaded on PC";
    }

    // ── Hotkey Settings ──

    [ObservableProperty]
    private string _hotkeyModifiers = "Control";

    [ObservableProperty]
    private string _hotkeyKey = "Space";

    [ObservableProperty]
    private string _promptHotkeyModifiers = "Control + Shift";

    [ObservableProperty]
    private string _promptHotkeyKey = "Space";

    // ── Privacy & Feature Settings ──

    [ObservableProperty]
    private bool _autoRunOcr = true;

    [ObservableProperty]
    private bool _saveHistory = true;

    [ObservableProperty]
    private bool _saveImagesInHistory = false;

    [ObservableProperty]
    private bool _autoSearchWeb = true;

    [ObservableProperty]
    private bool _startWithWindows = false;

    [ObservableProperty]
    private bool _startMinimizedToTray = false;

    [ObservableProperty]
    private bool _showTrayNotifications = true;

    [ObservableProperty]
    private string _newExcludedApp = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _excludedApplications = new();

    // ── Status ──

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isSuccessStatus;

    [ObservableProperty]
    private bool _isTestingConnection;

    public IReadOnlyList<string> AvailableModifiers => new[] { "Control", "Alt", "Shift", "Control + Alt", "Control + Shift" };
    public IReadOnlyList<string> AvailableKeys => new[] { "Space", "S", "Q", "E", "F1", "F2", "F3", "F4", "F8", "F9", "F10", "F11", "F12" };

    public SettingsViewModel(
        ISettingsService settingsService,
        IAiProviderFactory aiProviderFactory,
        IHotkeyService hotkeyService,
        IStartupService startupService,
        IOllamaManagementService ollamaService)
    {
        _settingsService = settingsService;
        _aiProviderFactory = aiProviderFactory;
        _hotkeyService = hotkeyService;
        _startupService = startupService;
        _ollamaService = ollamaService;

        var geminiProvider = _aiProviderFactory.GetProvider("gemini");
        foreach (var m in geminiProvider.SupportedModels)
        {
            AvailableModels.Add(m);
        }

        // Populate recommended Ollama models
        foreach (var m in _ollamaService.GetRecommendedVisionModels())
        {
            OllamaModels.Add(m);
        }
    }

    public async Task InitializeAsync()
    {
        var settings = await _settingsService.LoadSettingsAsync();
        string? key = await _settingsService.GetApiKeyAsync();

        // Provider state
        ActiveProvider = settings.ActiveProvider;
        OllamaBaseUrl = settings.OllamaBaseUrl;
        SelectedOllamaModel = OllamaManagementService.IsKnownVisionModel(settings.OllamaModel)
            ? settings.OllamaModel
            : "llava";

        // Gemini state
        ApiKey = key ?? string.Empty;
        SelectedModel = settings.SelectedModel;

        // Hotkeys
        HotkeyModifiers = settings.HotkeyModifiers;
        HotkeyKey = settings.HotkeyKey;
        PromptHotkeyModifiers = settings.PromptHotkeyModifiers;
        PromptHotkeyKey = settings.PromptHotkeyKey;

        // Features
        AutoRunOcr = settings.AutoRunOcr;
        SaveHistory = settings.SaveHistory;
        SaveImagesInHistory = settings.SaveImagesInHistory;
        AutoSearchWeb = settings.AutoSearchWeb;
        StartWithWindows = _startupService.IsStartupEnabled();
        StartMinimizedToTray = settings.StartMinimizedToTray;
        ShowTrayNotifications = settings.ShowTrayNotifications;

        ExcludedApplications.Clear();
        foreach (var app in settings.ExcludedApplications)
        {
            ExcludedApplications.Add(app);
        }

        // Fetch dynamic Gemini models if key is present
        if (!string.IsNullOrWhiteSpace(key))
        {
            var geminiProvider = _aiProviderFactory.GetProvider("gemini");
            var dynamicModels = await geminiProvider.GetAvailableModelsAsync(key);
            if (dynamicModels.Count > 0)
            {
                AvailableModels.Clear();
                foreach (var m in dynamicModels)
                {
                    AvailableModels.Add(m);
                }
            }
        }

        if (!AvailableModels.Contains(SelectedModel))
        {
            SelectedModel = AvailableModels.FirstOrDefault() ?? "gemini-flash-latest";
        }

        // Refresh Ollama status
        await RefreshOllamaStatusAsync();

        StatusMessage = string.Empty;
    }

    // ── Provider Toggle Commands ──

    [RelayCommand]
    public void SelectGemini()
    {
        ActiveProvider = "gemini";
    }

    [RelayCommand]
    public void SelectOllama()
    {
        ActiveProvider = "ollama";
    }

    // ── Ollama Management Commands ──

    [RelayCommand]
    public async Task RefreshOllamaStatusAsync()
    {
        IsOllamaInstalled = _ollamaService.IsOllamaInstalled();

        if (IsOllamaInstalled)
        {
            IsOllamaRunning = await _ollamaService.IsOllamaRunningAsync();
            OllamaStatusText = IsOllamaRunning ? "● Running" : "○ Not Running";

            if (IsOllamaRunning)
            {
                // Refresh installed models list
                var installedModels = await _ollamaService.GetInstalledModelsAsync();
                _installedVisionModels.Clear();
                foreach (var m in installedModels)
                {
                    _installedVisionModels.Add(m);
                }

                // Merge installed models with recommended ones (installed first)
                var merged = new List<string>(installedModels);
                foreach (var rec in _ollamaService.GetRecommendedVisionModels())
                {
                    if (!merged.Any(m => m.StartsWith(rec, StringComparison.OrdinalIgnoreCase)))
                    {
                        merged.Add(rec);
                    }
                }
                OllamaModels.Clear();
                foreach (var m in merged)
                {
                    OllamaModels.Add(m);
                }

                if (!OllamaModels.Contains(SelectedOllamaModel) || !OllamaManagementService.IsKnownVisionModel(SelectedOllamaModel))
                {
                    SelectedOllamaModel = OllamaModels.FirstOrDefault(m => OllamaManagementService.IsKnownVisionModel(m)) ?? "llava";
                }

                UpdateSelectedModelInstallationStatus();
            }
        }
        else
        {
            IsOllamaRunning = false;
            OllamaStatusText = "⚠ Not Installed";
            UpdateSelectedModelInstallationStatus();
        }
    }

    [RelayCommand]
    public async Task InstallOllamaAsync()
    {
        IsInstallingOllama = true;
        OllamaInstallProgress = 0;
        StatusMessage = "Downloading Ollama installer...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                OllamaInstallProgress = p * 100;
                if (p < 0.7)
                    StatusMessage = $"Downloading Ollama... {p * 100:F0}%";
                else if (p < 0.95)
                    StatusMessage = "Installing Ollama...";
                else
                    StatusMessage = "Finalizing installation...";
            });

            await _ollamaService.InstallOllamaAsync(progress);

            StatusMessage = "✅ Ollama installed successfully!";
            IsSuccessStatus = true;

            // Refresh status
            await RefreshOllamaStatusAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Installation failed: {ex.Message}";
            IsSuccessStatus = false;
        }
        finally
        {
            IsInstallingOllama = false;
        }
    }

    [RelayCommand]
    public async Task StartOllamaAsync()
    {
        StatusMessage = "Starting Ollama server...";
        try
        {
            await _ollamaService.StartOllamaAsync();
            StatusMessage = "✅ Ollama is now running!";
            IsSuccessStatus = true;
            await RefreshOllamaStatusAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Could not start Ollama: {ex.Message}";
            IsSuccessStatus = false;
        }
    }

    [RelayCommand]
    public async Task PullModelAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedOllamaModel)) return;

        IsPullingModel = true;
        OllamaPullProgress = 0;
        OllamaPullStatus = $"Downloading {SelectedOllamaModel}...";

        try
        {
            var progress = new Progress<(string Status, double Percent)>(p =>
            {
                OllamaPullProgress = p.Percent * 100;
                OllamaPullStatus = p.Status == "success"
                    ? "✅ Download complete!"
                    : $"{p.Status}... {p.Percent * 100:F0}%";
            });

            await _ollamaService.PullModelAsync(SelectedOllamaModel, progress);

            StatusMessage = $"✅ Model '{SelectedOllamaModel}' is ready!";
            IsSuccessStatus = true;
            OllamaPullStatus = "✅ Download complete!";

            // Refresh installed models list
            await RefreshOllamaStatusAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Model download failed: {ex.Message}";
            OllamaPullStatus = $"❌ Failed: {ex.Message}";
            IsSuccessStatus = false;
        }
        finally
        {
            IsPullingModel = false;
        }
    }

    [RelayCommand]
    public async Task DeleteModelAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedOllamaModel)) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the vision model '{SelectedOllamaModel}' from your PC?\n\nThis will free up disk space.",
            "Delete Model",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        StatusMessage = $"Deleting {SelectedOllamaModel}...";
        try
        {
            await _ollamaService.DeleteModelAsync(SelectedOllamaModel);
            StatusMessage = $"✅ Model '{SelectedOllamaModel}' deleted successfully.";
            IsSuccessStatus = true;
            await RefreshOllamaStatusAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Failed to delete model: {ex.Message}";
            IsSuccessStatus = false;
        }
    }

    [RelayCommand]
    public async Task UninstallOllamaAsync()
    {
        var result = MessageBox.Show(
            "Are you sure you want to uninstall Ollama from this computer?\n\nThis will terminate Ollama services and remove the installation.",
            "Uninstall Ollama",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        StatusMessage = "Uninstalling Ollama...";
        try
        {
            await _ollamaService.UninstallOllamaAsync();
            StatusMessage = "✅ Ollama uninstalled successfully.";
            IsSuccessStatus = true;
            await RefreshOllamaStatusAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Failed to uninstall Ollama: {ex.Message}";
            IsSuccessStatus = false;
        }
    }

    // ── Gemini Commands ──

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = "Please enter an API key first.";
            MessageBox.Show("Please paste your Gemini API key into the text box first.", "Relay Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsTestingConnection = true;
        StatusMessage = "Testing Gemini API connection...";

        try
        {
            var geminiProvider = _aiProviderFactory.GetProvider("gemini");
            bool valid = await geminiProvider.ValidateCredentialsAsync(ApiKey.Trim());
            if (valid)
            {
                // Refresh models list dynamically for this key
                var dynamicModels = await geminiProvider.GetAvailableModelsAsync(ApiKey.Trim());
                if (dynamicModels.Count > 0)
                {
                    AvailableModels.Clear();
                    foreach (var m in dynamicModels)
                    {
                        AvailableModels.Add(m);
                    }
                }

                if (!AvailableModels.Contains(SelectedModel))
                {
                    SelectedModel = AvailableModels.FirstOrDefault() ?? "gemini-2.0-flash";
                }

                StatusMessage = "✅ Connection successful! Gemini API is responsive.";
                IsSuccessStatus = true;
                MessageBox.Show($"✅ Connection successful!\n\nYour Gemini API key is valid and working.\nFound {AvailableModels.Count} available models on your account.", "Relay Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = "❌ Connection failed. Please check your API key.";
                IsSuccessStatus = false;
                MessageBox.Show("❌ Connection failed.\n\nPlease check that your Gemini API key is valid and has active quota.", "Relay Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error: {ex.Message}";
            IsSuccessStatus = false;
            MessageBox.Show($"❌ Connection Error:\n\n{ex.Message}", "Relay Settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    // ── Save / Load ──

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        var settings = _settingsService.CurrentSettings;
        settings.ActiveProvider = ActiveProvider;
        settings.OllamaBaseUrl = OllamaBaseUrl;
        settings.OllamaModel = SelectedOllamaModel;
        settings.SelectedModel = SelectedModel;
        settings.HotkeyModifiers = HotkeyModifiers;
        settings.HotkeyKey = HotkeyKey;
        settings.PromptHotkeyModifiers = PromptHotkeyModifiers;
        settings.PromptHotkeyKey = PromptHotkeyKey;
        settings.AutoRunOcr = AutoRunOcr;
        settings.SaveHistory = SaveHistory;
        settings.SaveImagesInHistory = SaveImagesInHistory;
        settings.AutoSearchWeb = AutoSearchWeb;
        settings.StartWithWindows = StartWithWindows;
        settings.StartMinimizedToTray = StartMinimizedToTray;
        settings.ShowTrayNotifications = ShowTrayNotifications;
        settings.ExcludedApplications = ExcludedApplications.ToList();

        _startupService.SetStartup(StartWithWindows, startMinimized: true);

        // Always persist all settings first
        await _settingsService.SaveSettingsAsync(settings);

        // Then encrypt and store the API key (re-saves settings with the encrypted key)
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            await _settingsService.SetApiKeyAsync(ApiKey.Trim());
        }

        // Re-register hotkeys immediately
        var (mod1, key1) = HotkeyParser.Parse(settings.HotkeyModifiers, settings.HotkeyKey);
        var (mod2, key2) = HotkeyParser.Parse(settings.PromptHotkeyModifiers, settings.PromptHotkeyKey);
        _hotkeyService.UnregisterAll();
        _hotkeyService.RegisterHotkey(mod1, key1, id: 9001);
        _hotkeyService.RegisterHotkey(mod2, key2, id: 9002);

        StatusMessage = "✅ Settings saved successfully!";
        IsSuccessStatus = true;
        MessageBox.Show("✅ Settings saved successfully!", "Relay", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    public void AddExcludedApp()
    {
        if (!string.IsNullOrWhiteSpace(NewExcludedApp) && !ExcludedApplications.Contains(NewExcludedApp.Trim()))
        {
            ExcludedApplications.Add(NewExcludedApp.Trim());
            NewExcludedApp = string.Empty;
        }
    }

    [RelayCommand]
    public void RemoveExcludedApp(string app)
    {
        if (ExcludedApplications.Contains(app))
        {
            ExcludedApplications.Remove(app);
        }
    }

    [RelayCommand]
    public void ToggleShowApiKey()
    {
        ShowApiKey = !ShowApiKey;
    }
}
