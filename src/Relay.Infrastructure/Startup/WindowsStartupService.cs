using System.Diagnostics;
using Microsoft.Win32;
using Relay.Core.Interfaces;

namespace Relay.Infrastructure.Startup;

/// <summary>
/// Manages Windows startup registry entry under HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
public class WindowsStartupService : IStartupService
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Relay";

    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            if (key == null) return false;

            var val = key.GetValue(AppName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public bool SetStartup(bool enable, bool startMinimized = true)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunRegistryKey, true);
            if (key == null) return false;

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    exePath = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrWhiteSpace(exePath))
                {
                    exePath = "Relay.exe";
                }

                string command = startMinimized ? $"\"{exePath}\" --minimized" : $"\"{exePath}\"";
                key.SetValue(AppName, command);
                return true;
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                }
                return true;
            }
        }
        catch
        {
            return false;
        }
    }
}
