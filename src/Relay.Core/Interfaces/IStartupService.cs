namespace Relay.Core.Interfaces;

/// <summary>
/// Service to manage application startup with Windows login and background execution.
/// </summary>
public interface IStartupService
{
    /// <summary>
    /// Checks whether Relay is configured to start automatically on Windows login.
    /// </summary>
    bool IsStartupEnabled();

    /// <summary>
    /// Enables or disables automatic startup on Windows login.
    /// </summary>
    /// <param name="enable">Whether to enable auto-start.</param>
    /// <param name="startMinimized">Whether to launch with --minimized argument.</param>
    bool SetStartup(bool enable, bool startMinimized = true);
}
