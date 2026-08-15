using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenLens.Core.Interfaces;
using ScreenLens.Core.Models;

namespace ScreenLens.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IAiProvider _aiProvider;
    private readonly IHotkeyService _hotkeyService;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private bool _showApiKey;

    [ObservableProperty]
    private string _selectedModel = "gemini-3.5-flash-lite";

    [ObservableProperty]
    private string _hotkeyModifiers = "Control";

    [ObservableProperty]
    private string _hotkeyKey = "Space";

    [ObservableProperty]
    private bool _autoRunOcr = true;

    [ObservableProperty]
    private bool _saveHistory = true;

    [ObservableProperty]
    private bool _saveImagesInHistory = false;

    [ObservableProperty]
    private bool _autoSearchWeb = true;

    [ObservableProperty]
    private string _newExcludedApp = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _excludedApplications = new();

    [ObservableProperty]
    private ObservableCollection<string> _availableModels = new();

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
        IAiProvider aiProvider,
        IHotkeyService hotkeyService)
    {
        _settingsService = settingsService;
        _aiProvider = aiProvider;
        _hotkeyService = hotkeyService;

        foreach (var m in _aiProvider.SupportedModels)
        {
            AvailableModels.Add(m);
        }
    }

    public async Task InitializeAsync()
    {
        var settings = await _settingsService.LoadSettingsAsync();
        string? key = await _settingsService.GetApiKeyAsync();

        ApiKey = key ?? string.Empty;
        SelectedModel = settings.SelectedModel;
        HotkeyModifiers = settings.HotkeyModifiers;
        HotkeyKey = settings.HotkeyKey;
        AutoRunOcr = settings.AutoRunOcr;
        SaveHistory = settings.SaveHistory;
        SaveImagesInHistory = settings.SaveImagesInHistory;
        AutoSearchWeb = settings.AutoSearchWeb;

        ExcludedApplications.Clear();
        foreach (var app in settings.ExcludedApplications)
        {
            ExcludedApplications.Add(app);
        }

        // Fetch dynamic models if key is present
        if (!string.IsNullOrWhiteSpace(key))
        {
            var dynamicModels = await _aiProvider.GetAvailableModelsAsync(key);
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
            SelectedModel = AvailableModels.FirstOrDefault() ?? "gemini-3.5-flash-lite";
        }

        StatusMessage = string.Empty;
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = "Please enter an API key first.";
            MessageBox.Show("Please paste your Gemini API key into the text box first.", "ScreenLens Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsTestingConnection = true;
        StatusMessage = "Testing Gemini API connection...";

        try
        {
            bool valid = await _aiProvider.ValidateCredentialsAsync(ApiKey.Trim());
            if (valid)
            {
                // Refresh models list dynamically for this key
                var dynamicModels = await _aiProvider.GetAvailableModelsAsync(ApiKey.Trim());
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
                    SelectedModel = AvailableModels.FirstOrDefault() ?? "gemini-3.5-flash-lite";
                }

                StatusMessage = "✅ Connection successful! Gemini API is responsive.";
                IsSuccessStatus = true;
                MessageBox.Show($"✅ Connection successful!\n\nYour Gemini API key is valid and working.\nFound {AvailableModels.Count} available models on your account.", "ScreenLens Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = "❌ Connection failed. Please check your API key.";
                IsSuccessStatus = false;
                MessageBox.Show("❌ Connection failed.\n\nPlease check that your Gemini API key is valid and has active quota.", "ScreenLens Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error: {ex.Message}";
            IsSuccessStatus = false;
            MessageBox.Show($"❌ Connection Error:\n\n{ex.Message}", "ScreenLens Settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        var settings = _settingsService.CurrentSettings;
        settings.SelectedModel = SelectedModel;
        settings.HotkeyModifiers = HotkeyModifiers;
        settings.HotkeyKey = HotkeyKey;
        settings.AutoRunOcr = AutoRunOcr;
        settings.SaveHistory = SaveHistory;
        settings.SaveImagesInHistory = SaveImagesInHistory;
        settings.AutoSearchWeb = AutoSearchWeb;
        settings.ExcludedApplications = ExcludedApplications.ToList();

        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            await _settingsService.SetApiKeyAsync(ApiKey.Trim());
        }
        else
        {
            await _settingsService.SaveSettingsAsync(settings);
        }

        StatusMessage = "✅ Settings saved successfully!";
        IsSuccessStatus = true;
        MessageBox.Show("✅ Settings saved successfully!", "ScreenLens", MessageBoxButton.OK, MessageBoxImage.Information);
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
