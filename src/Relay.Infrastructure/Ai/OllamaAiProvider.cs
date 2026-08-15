using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Relay.Core.Interfaces;
using Relay.Core.Models;
using Relay.Infrastructure.Ai.Prompts;

namespace Relay.Infrastructure.Ai;

public class OllamaAiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;

    public string ProviderId => "ollama";
    public string DisplayName => "Local AI (Ollama)";
    public IReadOnlyList<string> SupportedModels => new[]
    {
        "llama3.2-vision",
        "llava",
        "moondream",
        "minicpm-v",
        "qwen2-vl",
        "bakllava"
    };

    public OllamaAiProvider(HttpClient httpClient, ISettingsService settingsService)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
    }

    private string BaseUrl => _settingsService.CurrentSettings?.OllamaBaseUrl?.TrimEnd('/') ?? "http://localhost:11434";

    public async Task<bool> ValidateCredentialsAsync(string? apiKey = null, CancellationToken ct = default)
    {
        // Ollama doesn't need an API key — just check if the server is reachable
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

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(string? apiKey = null, CancellationToken ct = default)
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
                        if (!string.IsNullOrEmpty(name) && OllamaManagementService.IsKnownVisionModel(name))
                        {
                            // Keep vision models
                            result.Add(name);
                        }
                    }

                    if (result.Count > 0)
                    {
                        return result.OrderBy(m => m).ToList();
                    }
                }
            }
        }
        catch
        {
            // Fallback to static list
        }

        return SupportedModels;
    }

    public async IAsyncEnumerable<AiStreamChunk> AnalyzeStreamAsync(
        AiAnalysisRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string initialModel = _settingsService.CurrentSettings?.OllamaModel ?? "llava";

        // Candidate fallback vision models in priority order (broadest hardware & Ollama version compatibility)
        var candidateModels = new List<string>();
        if (!string.IsNullOrWhiteSpace(initialModel))
        {
            candidateModels.Add(initialModel);
        }
        foreach (var fallback in new[] { "moondream", "llava", "llava:7b", "minicpm-v", "qwen2-vl", "bakllava" })
        {
            if (!candidateModels.Any(c => c.Equals(fallback, StringComparison.OrdinalIgnoreCase)))
            {
                candidateModels.Add(fallback);
            }
        }

        // Check connectivity
        bool reachable = await ValidateCredentialsAsync(ct: ct);
        if (!reachable)
        {
            yield return new AiStreamChunk
            {
                IsComplete = true,
                ErrorMessage = "Ollama is not running. Please start Ollama from the Settings panel or run 'ollama serve' in a terminal."
            };
            yield break;
        }

        // Build messages array
        var messagesArray = new JsonArray();

        // System message
        messagesArray.Add(new JsonObject
        {
            ["role"] = "system",
            ["content"] = RelayPrompts.SystemInstruction
        });

        // Conversation history
        foreach (var chat in request.ConversationHistory)
        {
            messagesArray.Add(new JsonObject
            {
                ["role"] = chat.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant",
                ["content"] = chat.Content
            });
        }

        // Current turn — text + optional image
        string promptText = RelayPrompts.BuildUserPrompt(request);
        var currentMessage = new JsonObject
        {
            ["role"] = "user",
            ["content"] = promptText
        };

        // Attach image if available
        if (request.Region.ImageBytes.Length > 0)
        {
            string base64Image = Convert.ToBase64String(request.Region.ImageBytes);
            currentMessage["images"] = new JsonArray { base64Image };
        }

        messagesArray.Add(currentMessage);

        string url = $"{BaseUrl}/api/chat";
        HttpResponseMessage? response = null;
        string? connectionError = null;
        string activeModel = initialModel;

        foreach (var tryModel in candidateModels)
        {
            if (ct.IsCancellationRequested) break;

            var payload = new JsonObject
            {
                ["model"] = tryModel,
                ["messages"] = messagesArray,
                ["stream"] = true,
                ["options"] = new JsonObject
                {
                    ["temperature"] = 0.2
                }
            };

            try
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
                };

                response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    activeModel = tryModel;
                    connectionError = null;

                    // If we successfully recovered using a fallback model, persist it to user settings
                    if (!tryModel.Equals(initialModel, StringComparison.OrdinalIgnoreCase))
                    {
                        var curSettings = _settingsService.CurrentSettings;
                        if (curSettings != null)
                        {
                            curSettings.OllamaModel = tryModel;
                            _ = Task.Run(async () =>
                            {
                                try { await _settingsService.SaveSettingsAsync(curSettings, CancellationToken.None); } catch { }
                            });
                        }
                    }
                    break;
                }

                string errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                response.Dispose();
                response = null;

                // If error indicates architecture, mllama, crash or missing model, try next candidate
                if (errorBody.Contains("unknown model architecture", StringComparison.OrdinalIgnoreCase) ||
                    errorBody.Contains("mllama", StringComparison.OrdinalIgnoreCase) ||
                    errorBody.Contains("llama-server process has terminated", StringComparison.OrdinalIgnoreCase) ||
                    errorBody.Contains("does not support images", StringComparison.OrdinalIgnoreCase) ||
                    errorBody.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                    response?.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    continue;
                }
                else
                {
                    connectionError = $"Ollama API error: {errorBody}";
                    break;
                }
            }
            catch (Exception ex)
            {
                connectionError = $"Could not connect to Ollama at {BaseUrl}: {ex.Message}";
                break;
            }
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            yield return new AiStreamChunk
            {
                IsComplete = true,
                ErrorMessage = connectionError ?? "No compatible local vision model could be loaded. Please ensure 'llava' or 'moondream' is downloaded in Settings."
            };
            yield break;
        }

        // Stream NDJSON response
        using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(responseStream);

        var fullAccumulatedText = new StringBuilder();
        var streamBuffer = new StringBuilder();
        bool headerParsed = false;
        IntentType detectedIntent = IntentType.General;
        string parsedTitle = string.Empty;
        string parsedSummary = string.Empty;
        List<ActionItem> parsedActions = new();

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Each line is a complete JSON object (NDJSON)
            string? chunkText = ExtractTextFromNdjsonChunk(line);
            bool isDone = IsChunkDone(line);

            if (!string.IsNullOrEmpty(chunkText))
            {
                fullAccumulatedText.Append(chunkText);
                streamBuffer.Append(chunkText);

                if (!headerParsed)
                {
                    string currentTotal = streamBuffer.ToString();
                    int contentDelimiterIdx = currentTotal.IndexOf("---CONTENT---", StringComparison.Ordinal);

                    if (contentDelimiterIdx >= 0)
                    {
                        string jsonBlock = currentTotal.Substring(0, contentDelimiterIdx);
                        ParseHeaderJson(jsonBlock, out detectedIntent, out parsedTitle, out parsedSummary, out parsedActions);
                        headerParsed = true;

                        string remainingContent = currentTotal.Substring(contentDelimiterIdx + "---CONTENT---".Length).TrimStart();
                        streamBuffer.Clear();

                        yield return new AiStreamChunk
                        {
                            TextDelta = remainingContent,
                            DetectedIntent = detectedIntent,
                            Title = parsedTitle,
                            Summary = parsedSummary,
                            ActionItems = parsedActions,
                            IsComplete = false
                        };
                    }
                }
                else
                {
                    yield return new AiStreamChunk
                    {
                        TextDelta = chunkText,
                        DetectedIntent = detectedIntent,
                        Title = parsedTitle,
                        Summary = parsedSummary,
                        ActionItems = parsedActions,
                        IsComplete = false
                    };
                }
            }

            if (isDone) break;
        }

        // Final completion chunk
        if (!headerParsed)
        {
            string full = fullAccumulatedText.ToString();
            ParseHeaderJson(full, out detectedIntent, out parsedTitle, out parsedSummary, out parsedActions);
        }

        yield return new AiStreamChunk
        {
            IsComplete = true,
            DetectedIntent = detectedIntent,
            Title = parsedTitle,
            Summary = parsedSummary,
            ActionItems = parsedActions
        };
    }

    public async Task<AiAnalysisResult> AnalyzeAsync(AiAnalysisRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var markdownBuilder = new StringBuilder();
        IntentType intent = IntentType.General;
        string title = string.Empty;
        string summary = string.Empty;
        List<ActionItem> actionItems = new();
        string? error = null;

        await foreach (var chunk in AnalyzeStreamAsync(request, ct))
        {
            if (chunk.ErrorMessage != null) error = chunk.ErrorMessage;
            if (chunk.DetectedIntent.HasValue) intent = chunk.DetectedIntent.Value;
            if (!string.IsNullOrEmpty(chunk.Title)) title = chunk.Title;
            if (!string.IsNullOrEmpty(chunk.Summary)) summary = chunk.Summary;
            if (chunk.ActionItems != null && chunk.ActionItems.Count > 0) actionItems = chunk.ActionItems.ToList();
            if (!string.IsNullOrEmpty(chunk.TextDelta)) markdownBuilder.Append(chunk.TextDelta);
        }

        sw.Stop();

        if (error != null)
        {
            return new AiAnalysisResult
            {
                DetectedIntent = IntentType.General,
                Title = "Error",
                Summary = error,
                MarkdownContent = $"⚠️ **Error encountered:** {error}",
                ElapsedTime = sw.Elapsed
            };
        }

        return new AiAnalysisResult
        {
            DetectedIntent = intent,
            Title = string.IsNullOrEmpty(title) ? "Analysis Complete" : title,
            Summary = summary,
            MarkdownContent = markdownBuilder.ToString(),
            ActionItems = actionItems,
            ElapsedTime = sw.Elapsed
        };
    }

    internal static string? ExtractTextFromNdjsonChunk(string ndjsonLine)
    {
        try
        {
            var node = JsonNode.Parse(ndjsonLine);
            return node?["message"]?["content"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    internal static bool IsChunkDone(string ndjsonLine)
    {
        try
        {
            var node = JsonNode.Parse(ndjsonLine);
            return node?["done"]?.GetValue<bool>() ?? false;
        }
        catch
        {
            return false;
        }
    }

    internal static void ParseHeaderJson(
        string rawHeader,
        out IntentType intent,
        out string title,
        out string summary,
        out List<ActionItem> actions)
    {
        intent = IntentType.General;
        title = string.Empty;
        summary = string.Empty;
        actions = new List<ActionItem>();

        try
        {
            string clean = rawHeader.Trim();
            int startIdx = clean.IndexOf('{');
            int endIdx = clean.LastIndexOf('}');

            if (startIdx >= 0 && endIdx > startIdx)
            {
                string jsonStr = clean.Substring(startIdx, endIdx - startIdx + 1);
                var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                if (root.TryGetProperty("intent", out var intentProp))
                {
                    string intentStr = intentProp.GetString() ?? "";
                    if (Enum.TryParse<IntentType>(intentStr, true, out var parsed))
                    {
                        intent = parsed;
                    }
                }

                if (root.TryGetProperty("title", out var titleProp))
                {
                    title = titleProp.GetString() ?? "";
                }

                if (root.TryGetProperty("summary", out var summaryProp))
                {
                    summary = summaryProp.GetString() ?? "";
                }

                if (root.TryGetProperty("actionItems", out var actionsProp) && actionsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in actionsProp.EnumerateArray())
                    {
                        string label = item.TryGetProperty("label", out var lp) ? lp.GetString() ?? "" : "";
                        string actionType = item.TryGetProperty("actionType", out var atp) ? atp.GetString() ?? "COPY" : "COPY";
                        string? payload = item.TryGetProperty("payload", out var pp) ? pp.GetString() : null;
                        string? icon = item.TryGetProperty("icon", out var ip) ? ip.GetString() : null;

                        if (!string.IsNullOrEmpty(label))
                        {
                            actions.Add(new ActionItem(label, actionType, payload, icon));
                        }
                    }
                }
            }
        }
        catch
        {
            // If JSON fails to parse, leave defaults
        }
    }
}
