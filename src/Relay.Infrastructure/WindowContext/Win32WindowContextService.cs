using System.Diagnostics;
using System.Text;
using Relay.Core.Interfaces;
using Relay.Core.Models;
using Relay.Infrastructure.ScreenCapture;

namespace Relay.Infrastructure.WindowContext;

public class Win32WindowContextService : IWindowContextService
{
    private static readonly Dictionary<string, string> KnownProcessAppNames = new(StringComparer.OrdinalIgnoreCase)
    {
        { "devenv", "Visual Studio" },
        { "code", "Visual Studio Code" },
        { "chrome", "Google Chrome" },
        { "msedge", "Microsoft Edge" },
        { "firefox", "Mozilla Firefox" },
        { "brave", "Brave Browser" },
        { "explorer", "File Explorer" },
        { "spotify", "Spotify" },
        { "discord", "Discord" },
        { "slack", "Slack" },
        { "teams", "Microsoft Teams" },
        { "notion", "Notion" },
        { "figma", "Figma" },
        { "photoshop", "Adobe Photoshop" },
        { "illustrator", "Adobe Illustrator" },
        { "premiere", "Adobe Premiere Pro" },
        { "obs64", "OBS Studio" },
        { "steam", "Steam" },
        { "javaw", "Java / Minecraft" },
        { "minecraft", "Minecraft" },
        { "windowsterminal", "Windows Terminal" },
        { "powershell", "PowerShell" },
        { "cmd", "Command Prompt" },
        { "notepad", "Notepad" },
        { "word", "Microsoft Word" },
        { "excel", "Microsoft Excel" },
        { "powerpnt", "Microsoft PowerPoint" },
        { "acrord32", "Adobe Acrobat Reader" },
        { "acrobat", "Adobe Acrobat" }
    };

    private readonly ISettingsService _settingsService;

    public Win32WindowContextService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public ScreenContext GetForegroundWindowContext()
    {
        try
        {
            IntPtr hWnd = NativeMethods.GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
            {
                return new ScreenContext();
            }

            var titleBuilder = new StringBuilder(512);
            NativeMethods.GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
            string windowTitle = titleBuilder.ToString();

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);

            string processName = string.Empty;
            string appName = string.Empty;

            if (processId > 0)
            {
                try
                {
                    using var proc = Process.GetProcessById((int)processId);
                    processName = proc.ProcessName;

                    if (KnownProcessAppNames.TryGetValue(processName, out string? friendlyName))
                    {
                        appName = friendlyName;
                    }
                    else if (!string.IsNullOrWhiteSpace(proc.MainWindowTitle))
                    {
                        appName = proc.ProcessName;
                    }
                    else
                    {
                        appName = proc.ProcessName;
                    }
                }
                catch
                {
                    processName = "Unknown";
                    appName = "Windows Application";
                }
            }

            bool isExcluded = IsApplicationExcluded(processName);

            return new ScreenContext
            {
                ApplicationName = appName,
                ProcessName = processName,
                WindowTitle = windowTitle,
                ProcessId = (int)processId,
                Timestamp = DateTime.UtcNow,
                IsExcludedApplication = isExcluded
            };
        }
        catch
        {
            return new ScreenContext();
        }
    }

    public bool IsApplicationExcluded(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        var excludedList = _settingsService.CurrentSettings?.ExcludedApplications;
        if (excludedList == null || excludedList.Count == 0) return false;

        return excludedList.Any(ex => string.Equals(ex, processName, StringComparison.OrdinalIgnoreCase));
    }
}
