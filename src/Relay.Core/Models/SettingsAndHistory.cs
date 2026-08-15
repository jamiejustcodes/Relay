namespace Relay.Core.Models;

/// <summary>
/// User and application preferences.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Active AI provider: "gemini" or "ollama".
    /// </summary>
    public string ActiveProvider { get; set; } = "gemini";
    public string EncryptedApiKey { get; set; } = string.Empty;
    public string SelectedModel { get; set; } = "gemini-flash-latest";
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llava";
    public string HotkeyModifiers { get; set; } = "Control";
    public string HotkeyKey { get; set; } = "Space";
    public string PromptHotkeyModifiers { get; set; } = "Control + Shift";
    public string PromptHotkeyKey { get; set; } = "Space";
    public bool AutoRunOcr { get; set; } = true;
    public bool SaveHistory { get; set; } = true;
    public bool SaveImagesInHistory { get; set; } = false;
    public int MaxImageDimension { get; set; } = 1568;
    public string Theme { get; set; } = "Dark";
    public bool AutoSearchWeb { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimizedToTray { get; set; } = false;
    public bool ShowTrayNotifications { get; set; } = true;
    public List<string> ExcludedApplications { get; set; } = new()
    {
        "1Password",
        "Bitwarden",
        "KeePass",
        "LastPass",
        "KeePassXC",
        "LockApp",
        "CredentialUIBroker"
    };
}

/// <summary>
/// Query and analysis log entry.
/// </summary>
public class HistoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ApplicationName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public IntentType Intent { get; set; } = IntentType.General;
    public string UserQuestion { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string MarkdownResponse { get; set; } = string.Empty;
    public string? ThumbnailBase64 { get; set; }
    public bool IsFavorite { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
}

/// <summary>
/// Web search result item.
/// </summary>
public record SearchResultItem(
    string Title,
    string Url,
    string Snippet,
    string? DisplayUrl = null,
    string? Price = null
);
