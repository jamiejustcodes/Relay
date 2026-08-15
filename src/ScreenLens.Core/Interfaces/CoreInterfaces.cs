using ScreenLens.Core.Models;

namespace ScreenLens.Core.Interfaces;

/// <summary>
/// Pluggable AI provider contract for multimodal analysis, intent detection, and streaming.
/// </summary>
public interface IAiProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    IReadOnlyList<string> SupportedModels { get; }

    Task<bool> ValidateCredentialsAsync(string? apiKey = null, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetAvailableModelsAsync(string? apiKey = null, CancellationToken ct = default);

    IAsyncEnumerable<AiStreamChunk> AnalyzeStreamAsync(AiAnalysisRequest request, CancellationToken ct = default);

    Task<AiAnalysisResult> AnalyzeAsync(AiAnalysisRequest request, CancellationToken ct = default);
}

/// <summary>
/// High-performance display and rectangular region capture service.
/// </summary>
public interface IScreenCaptureService
{
    IReadOnlyList<DisplayInfo> GetDisplays();

    Task<CaptureRegion> CaptureRegionAsync(int x, int y, int width, int height, double dpiScale = 1.0, CancellationToken ct = default);

    Task<CaptureRegion> CaptureVirtualScreenAsync(CancellationToken ct = default);
}

public class HotkeyPressedEventArgs : EventArgs
{
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}

/// <summary>
/// Global Windows keyboard shortcut listener.
/// </summary>
public interface IHotkeyService : IDisposable
{
    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    bool RegisterHotkey(uint modifiers, uint key);

    void UnregisterHotkey();

    bool IsRegistered { get; }
}

/// <summary>
/// Foreground window, process detection, and privacy filter service.
/// </summary>
public interface IWindowContextService
{
    ScreenContext GetForegroundWindowContext();

    bool IsApplicationExcluded(string processName);
}

/// <summary>
/// Local OCR extraction service.
/// </summary>
public interface IOcrService
{
    Task<string> RecognizeTextAsync(byte[] imageBytes, CancellationToken ct = default);
}

/// <summary>
/// Encryption and secure storage service (e.g. Windows DPAPI).
/// </summary>
public interface ISecretVault
{
    string EncryptSecret(string plainText);

    string DecryptSecret(string cipherText);
}

/// <summary>
/// Persistent settings management.
/// </summary>
public interface ISettingsService
{
    AppSettings CurrentSettings { get; }

    Task<AppSettings> LoadSettingsAsync(CancellationToken ct = default);

    Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default);

    Task<string?> GetApiKeyAsync(CancellationToken ct = default);

    Task SetApiKeyAsync(string plainApiKey, CancellationToken ct = default);
}

/// <summary>
/// Query history repository.
/// </summary>
public interface IHistoryRepository
{
    Task<IReadOnlyList<HistoryItem>> GetHistoryAsync(int limit = 50, string? searchFilter = null, CancellationToken ct = default);

    Task<HistoryItem?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task SaveHistoryItemAsync(HistoryItem item, CancellationToken ct = default);

    Task DeleteHistoryItemAsync(Guid id, CancellationToken ct = default);

    Task ClearAllHistoryAsync(CancellationToken ct = default);
}

/// <summary>
/// Web search integration service.
/// </summary>
public interface ISearchService
{
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(string query, CancellationToken ct = default);
}
