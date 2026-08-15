using System.IO;
using System.Text.Json;
using ScreenLens.Core.Interfaces;
using ScreenLens.Core.Models;

namespace ScreenLens.Infrastructure.Data;

public class SettingsService : ISettingsService
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Relay",
        "settings.json"
    );

    private static readonly string LegacySettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenLens",
        "settings.json"
    );

    private readonly ISecretVault _secretVault;
    private AppSettings _currentSettings;

    public AppSettings CurrentSettings => _currentSettings;

    public SettingsService(ISecretVault secretVault)
    {
        _secretVault = secretVault;
        _currentSettings = new AppSettings();
    }

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            string pathToRead = SettingsFilePath;
            if (!File.Exists(pathToRead) && File.Exists(LegacySettingsFilePath))
            {
                pathToRead = LegacySettingsFilePath;
            }

            if (File.Exists(pathToRead))
            {
                string json = await File.ReadAllTextAsync(pathToRead, ct);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    // Auto-migrate any deprecated/retired models (1.5, 2.0, 2.5) to fast gemini-3.5-flash-lite
                    if (string.IsNullOrWhiteSpace(loaded.SelectedModel) ||
                        loaded.SelectedModel.Contains("1.5") ||
                        loaded.SelectedModel.Contains("2.0") ||
                        loaded.SelectedModel.Contains("2.5"))
                    {
                        loaded.SelectedModel = "gemini-3.5-flash-lite";
                    }

                    _currentSettings = loaded;
                    await SaveSettingsAsync(_currentSettings, CancellationToken.None);
                }
            }
            else
            {
                _currentSettings = new AppSettings();
                await SaveSettingsAsync(_currentSettings, ct);
            }
        }
        catch
        {
            _currentSettings = new AppSettings();
        }

        return _currentSettings;
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default)
    {
        _currentSettings = settings;

        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(SettingsFilePath, json, ct);
        }
        catch
        {
            // Non-fatal logging
        }
    }

    public Task<string?> GetApiKeyAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(_currentSettings.EncryptedApiKey))
        {
            string decrypted = _secretVault.DecryptSecret(_currentSettings.EncryptedApiKey);
            if (!string.IsNullOrEmpty(decrypted))
            {
                return Task.FromResult<string?>(decrypted);
            }
        }

        string? envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        return Task.FromResult(envKey);
    }

    public async Task SetApiKeyAsync(string plainApiKey, CancellationToken ct = default)
    {
        string encrypted = _secretVault.EncryptSecret(plainApiKey);
        _currentSettings.EncryptedApiKey = encrypted;
        await SaveSettingsAsync(_currentSettings, ct);
    }
}
