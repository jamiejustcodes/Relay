using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Relay.Core.Interfaces;

namespace Relay.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IAiProviderFactory _aiProviderFactory;

    [ObservableProperty]
    private string _activeModel = "gemini-2.0-flash";

    [ObservableProperty]
    private string _activeHotkey = "Control + Space";

    [ObservableProperty]
    private string _activePromptHotkey = "Control + Shift + Space";

    [ObservableProperty]
    private bool _hasApiKey;

    [ObservableProperty]
    private string _statusText = "Ready & Listening";

    public event EventHandler? TriggerCaptureRequested;
    public event EventHandler? TriggerPromptCaptureRequested;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? OpenHistoryRequested;

    public MainWindowViewModel(ISettingsService settingsService, IAiProviderFactory aiProviderFactory)
    {
        _settingsService = settingsService;
        _aiProviderFactory = aiProviderFactory;
    }

    public async Task RefreshStateAsync()
    {
        var settings = await _settingsService.LoadSettingsAsync();
        string? key = await _settingsService.GetApiKeyAsync();

        HasApiKey = !string.IsNullOrWhiteSpace(key);
        bool isOllama = settings.ActiveProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase);
        var activeProvider = _aiProviderFactory.GetActiveProvider();
        ActiveModel = isOllama
            ? $"Ollama • {settings.OllamaModel}"
            : $"Gemini • {settings.SelectedModel}";
        ActiveHotkey = $"{settings.HotkeyModifiers} + {settings.HotkeyKey}".Replace("Control", "Ctrl");
        ActivePromptHotkey = $"{settings.PromptHotkeyModifiers} + {settings.PromptHotkeyKey}".Replace("Control", "Ctrl");
        StatusText = isOllama
            ? "Local AI • Ready & Listening"
            : (HasApiKey ? "Ready & Listening" : "API Key Required in Settings");
    }

    [RelayCommand]
    public void TriggerCapture()
    {
        TriggerCaptureRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void TriggerPromptCapture()
    {
        TriggerPromptCaptureRequested?.Invoke(this, EventArgs.Empty);
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
