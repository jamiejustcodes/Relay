using System.IO;
using System.Text.Json;
using Relay.Core.Interfaces;
using Relay.Core.Models;

namespace Relay.Infrastructure.Data;

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
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, options);
                if (loaded != null)
                {
                    // Auto-migrate any deprecated or non-working models to gemini-flash-latest
                    if (string.IsNullOrWhiteSpace(loaded.SelectedModel) ||
                        loaded.SelectedModel.StartsWith("gemini-1.", StringComparison.OrdinalIgnoreCase) ||
                        loaded.SelectedModel.StartsWith("gemini-2.", StringComparison.OrdinalIgnoreCase) ||
                        loaded.SelectedModel.Equals("gemini-pro", StringComparison.OrdinalIgnoreCase) ||
                        loaded.SelectedModel.Equals("gemini-1.0-pro", StringComparison.OrdinalIgnoreCase) ||
                        loaded.SelectedModel.Equals("gemini-pro-vision", StringComparison.OrdinalIgnoreCase) ||
                        loaded.SelectedModel.Contains("exp", StringComparison.OrdinalIgnoreCase))
                    {
                        loaded.SelectedModel = "gemini-flash-latest";
                    }

                    if (string.IsNullOrWhiteSpace(loaded.OllamaModel) ||
                        loaded.OllamaModel.StartsWith("llama3.2-vision", StringComparison.OrdinalIgnoreCase) ||
                        !Ai.OllamaManagementService.IsKnownVisionModel(loaded.OllamaModel))
                    {
                        loaded.OllamaModel = "llava";
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

            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            string json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(SettingsFilePath, json, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Save failed: {ex.Message}");
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
