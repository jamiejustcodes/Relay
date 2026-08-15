using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Relay.Core.Interfaces;

namespace Relay.Infrastructure.Ai;

public class OllamaManagementService : IOllamaManagementService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;

    private static readonly string[] KnownOllamaLocations = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ollama", "ollama.exe"),
    };

    private const string OllamaInstallerUrl = "https://ollama.com/download/OllamaSetup.exe";

    public OllamaManagementService(HttpClient httpClient, ISettingsService settingsService)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
    }

    private string BaseUrl => _settingsService.CurrentSettings?.OllamaBaseUrl?.TrimEnd('/') ?? "http://localhost:11434";

    public bool IsOllamaInstalled()
    {
        // 1. Check if 'ollama' is on PATH
        try
        {
            var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            foreach (var dir in pathDirs)
            {
                string candidate = Path.Combine(dir.Trim(), "ollama.exe");
                if (File.Exists(candidate))
                    return true;
            }
        }
        catch { }

        // 2. Check known install locations
        foreach (var loc in KnownOllamaLocations)
        {
            if (File.Exists(loc))
                return true;
        }

        return false;
    }

    public async Task<bool> IsOllamaRunningAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(BaseUrl, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task InstallOllamaAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "RelayOllamaInstall");
        Directory.CreateDirectory(tempDir);
        string installerPath = Path.Combine(tempDir, "OllamaSetup.exe");

        try
        {
            // Download the installer with progress reporting
            progress?.Report(0.0);

            using var response = await _httpClient.GetAsync(OllamaInstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[65536];
            long totalRead = 0;

            while (true)
            {
                int bytesRead = await contentStream.ReadAsync(buffer, ct);
                if (bytesRead == 0) break;

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;

                if (totalBytes.HasValue && totalBytes > 0)
                {
                    // Download phase is 0-70% of overall progress
                    double downloadPercent = (double)totalRead / totalBytes.Value;
                    progress?.Report(downloadPercent * 0.7);
                }
            }

            progress?.Report(0.75);

            // Run the silent installer
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                progress?.Report(0.85);
                await process.WaitForExitAsync(ct);
                progress?.Report(0.95);

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Ollama installer exited with code {process.ExitCode}.");
                }
            }

            progress?.Report(1.0);
        }
        finally
        {
            // Clean up installer
            try
            {
                if (File.Exists(installerPath))
                    File.Delete(installerPath);
            }
            catch { }
        }
    }

    public async Task StartOllamaAsync(CancellationToken ct = default)
    {
        // Find the ollama executable
        string? ollamaPath = FindOllamaExecutable();

        if (string.IsNullOrEmpty(ollamaPath))
        {
            throw new FileNotFoundException("Could not find ollama.exe. Please install Ollama first.");
        }

        // Check if already running
        if (await IsOllamaRunningAsync(ct))
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = ollamaPath,
            Arguments = "serve",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process.Start(startInfo);

        // Wait for server to become responsive (up to 15 seconds)
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(500, ct);
            if (await IsOllamaRunningAsync(ct))
                return;
        }

        throw new TimeoutException("Ollama started but did not respond within 15 seconds.");
    }

    public static bool IsKnownVisionModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return false;
        string lower = modelName.ToLowerInvariant();

        return lower.Contains("vision") ||
               lower.Contains("llava") ||
               lower.Contains("moondream") ||
               lower.Contains("minicpm-v") ||
               lower.Contains("bakllava") ||
               lower.Contains("qwen-vl") ||
               lower.Contains("qwen2-vl") ||
               lower.Contains("qwen2.5-vl") ||
               lower.Contains("qwenvl") ||
               lower.Contains("granite-vision") ||
               lower.Contains("granite3.2-vision") ||
               lower.Contains("internvl") ||
               lower.Contains("yi-vl") ||
               lower.Contains("xwin-vl") ||
               lower.Contains("mllama") ||
               lower.Contains("multimodal") ||
               lower.EndsWith("-vl") ||
               lower.Contains("-vl:") ||
               lower.Contains("-v:") ||
               lower.Contains(":vl");
    }

    private async Task<bool> CheckIfModelSupportsVisionAsync(string modelName, CancellationToken ct)
    {
        if (IsKnownVisionModel(modelName))
            return true;

        try
        {
            string url = $"{BaseUrl}/api/show";
            var payload = new JsonObject { ["name"] = modelName };
            var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(url, content, ct);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync(ct);
                var doc = JsonNode.Parse(json);

                // Check details -> families array (e.g. "clip", "mllama", "vision")
                var families = doc?["details"]?["families"]?.AsArray();
                if (families != null)
                {
                    foreach (var f in families)
                    {
                        string fam = f?.ToString()?.ToLowerInvariant() ?? "";
                        if (fam.Contains("clip") || fam.Contains("mllama") || fam.Contains("vision"))
                            return true;
                    }
                }

                // Check model_info keys for vision projector
                var modelInfo = doc?["model_info"]?.AsObject();
                if (modelInfo != null)
                {
                    foreach (var key in modelInfo)
                    {
                        if (key.Key.Contains("vision", StringComparison.OrdinalIgnoreCase) ||
                            key.Key.Contains("clip", StringComparison.OrdinalIgnoreCase) ||
                            key.Key.Contains("projector", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                // Check modelfile
                string modelfile = doc?["modelfile"]?.ToString() ?? "";
                if (modelfile.Contains("PROJECTOR", StringComparison.OrdinalIgnoreCase) ||
                    modelfile.Contains("vision", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    public async Task<IReadOnlyList<string>> GetInstalledModelsAsync(CancellationToken ct = default)
    {
        try
        {
            string url = $"{BaseUrl}/api/tags";
            using var response = await _httpClient.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync(ct);
                var doc = JsonNode.Parse(json);
                var modelsArray = doc?["models"]?.AsArray();
                if (modelsArray != null)
                {
                    var result = new List<string>();
                    foreach (var item in modelsArray)
                    {
                        string name = item?["name"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            bool isVision = await CheckIfModelSupportsVisionAsync(name, ct);
                            if (isVision)
                            {
                                result.Add(name);
                            }
                        }
                    }
                    return result.OrderBy(m => m).ToList();
                }
            }
        }
        catch { }

        return Array.Empty<string>();
    }

    public async Task PullModelAsync(string modelName, IProgress<(string Status, double Percent)>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name is required.", nameof(modelName));

        string url = $"{BaseUrl}/api/pull";
        var payload = new JsonObject
        {
            ["model"] = modelName,
            ["stream"] = true
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        string? line;
        while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var node = JsonNode.Parse(line);
                string status = node?["status"]?.ToString() ?? "";
                long total = node?["total"]?.GetValue<long>() ?? 0;
                long completed = node?["completed"]?.GetValue<long>() ?? 0;

                double percent = total > 0 ? (double)completed / total : 0;

                progress?.Report((status, percent));

                if (status == "success")
                {
                    progress?.Report(("success", 1.0));
                    return;
                }
            }
            catch { }
        }
    }

    public async Task DeleteModelAsync(string modelName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return;

        string url = $"{BaseUrl}/api/delete";
        var payload = new JsonObject { ["name"] = modelName };
        var httpRequest = new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task UninstallOllamaAsync(CancellationToken ct = default)
    {
        // 1. Terminate running Ollama processes
        try
        {
            foreach (var proc in Process.GetProcessesByName("ollama"))
            {
                try { proc.Kill(); } catch { }
            }
            foreach (var proc in Process.GetProcessesByName("ollama app"))
            {
                try { proc.Kill(); } catch { }
            }
            foreach (var proc in Process.GetProcessesByName("llama-server"))
            {
                try { proc.Kill(); } catch { }
            }
        }
        catch { }

        // 2. Locate uninstaller
        string[] uninstallerCandidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "unins000.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "unins000.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ollama", "unins000.exe"),
        };

        string? uninstallerPath = uninstallerCandidates.FirstOrDefault(File.Exists);

        if (!string.IsNullOrEmpty(uninstallerPath))
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = uninstallerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(startInfo);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            }
        }
        else
        {
            throw new FileNotFoundException("Ollama uninstaller executable was not found.");
        }
    }

    public IReadOnlyList<string> GetRecommendedVisionModels()
    {
        return new[]
        {
            "llava",
            "llama3.2-vision",
            "moondream",
            "minicpm-v",
            "qwen2-vl",
            "bakllava"
        };
    }

    private string? FindOllamaExecutable()
    {
        // Check PATH
        try
        {
            var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            foreach (var dir in pathDirs)
            {
                string candidate = Path.Combine(dir.Trim(), "ollama.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch { }

        // Check known locations
        foreach (var loc in KnownOllamaLocations)
        {
            if (File.Exists(loc))
                return loc;
        }

        return null;
    }
}
