namespace ScreenLens.Core.Models;

/// <summary>
/// Active application and operating system context at the moment of screen capture.
/// </summary>
public record ScreenContext
{
    public string ApplicationName { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string? LocalOcrText { get; init; }
    public string? ActiveUrl { get; init; }
    public string? SelectedOrClipboardText { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsExcludedApplication { get; init; }
}
