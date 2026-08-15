using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenLens.Core.Interfaces;

namespace ScreenLens.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IAiProvider _aiProvider;

    [ObservableProperty]
    private string _activeModel = "gemini-3.5-flash-lite";

    [ObservableProperty]
    private string _activeHotkey = "Ctrl + Space";

    [ObservableProperty]
    private bool _hasApiKey;

    [ObservableProperty]
    private string _statusText = "Ready & Listening";

    public event EventHandler? TriggerCaptureRequested;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? OpenHistoryRequested;

    public MainWindowViewModel(ISettingsService settingsService, IAiProvider aiProvider)
    {
        _settingsService = settingsService;
        _aiProvider = aiProvider;
    }

    public async Task RefreshStateAsync()
    {
        var settings = await _settingsService.LoadSettingsAsync();
        string? key = await _settingsService.GetApiKeyAsync();

        HasApiKey = !string.IsNullOrWhiteSpace(key);
        ActiveModel = settings.SelectedModel;
        ActiveHotkey = $"{settings.HotkeyModifiers} + {settings.HotkeyKey}";
        StatusText = HasApiKey ? "Ready & Listening (Ctrl + Space)" : "API Key Required in Settings";
    }

    [RelayCommand]
    public void TriggerCapture()
    {
        TriggerCaptureRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void OpenSettings()
    {
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void OpenHistory()
    {
        OpenHistoryRequested?.Invoke(this, EventArgs.Empty);
    }
}
