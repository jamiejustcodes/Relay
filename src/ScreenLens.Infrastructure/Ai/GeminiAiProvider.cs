using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenLens.Core.Interfaces;
using ScreenLens.Core.Models;
using ScreenLens.Infrastructure.Ai.Prompts;

namespace ScreenLens.Infrastructure.Ai;

public class GeminiAiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;

    public string ProviderId => "gemini";
    public string DisplayName => "Google Gemini";
    public IReadOnlyList<string> SupportedModels => new[]
    {
        "gemini-3.5-flash-lite",
        "gemini-flash-lite-latest",
        "gemini-3.1-flash-lite",
        "gemini-3.5-flash",
        "gemini-3.6-flash",
        "gemini-3.7-flash"
    };

    public GeminiAiProvider(HttpClient httpClient, ISettingsService settingsService)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
    }

    public async Task<bool> ValidateCredentialsAsync(string? apiKey = null, CancellationToken ct = default)
    {
        string? key = apiKey ?? await _settingsService.GetApiKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(key))
            return false;

        try
        {
            // Official Google AI Studio credentials validation endpoint
            string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={key.Trim()}";
            using var response = await _httpClient.GetAsync(url, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(string? apiKey = null, CancellationToken ct = default)
    {
        string? key = apiKey ?? await _settingsService.GetApiKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(key))
        {
            return SupportedModels;
        }

        try
        {
            string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={key.Trim()}";
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
                        var methods = item?["supportedGenerationMethods"]?.AsArray();
                        bool supportsGenerateContent = methods?.Any(m => m?.ToString() == "generateContent") ?? false;

                        if (supportsGenerateContent && !string.IsNullOrEmpty(name))
                        {
                            if (name.StartsWith("models/"))
                            {
                                name = name.Substring("models/".Length);
                            }

                            // Filter out non-multimodal / specialized / retired models
                            if (!name.Contains("tts") && !name.Contains("robotics") && !name.Contains("clip") &&
                                !name.Contains("1.5") && !name.Contains("2.5") && !name.Contains("2.0") &&
                                !name.Contains("banana") && !name.Contains("gemma"))
                            {
                                result.Add(name);
                            }
                        }
                    }

                    if (result.Count > 0)
                    {
                        return result
                            .OrderByDescending(m => m.Contains("3.5-flash-lite"))
                            .ThenByDescending(m => m.Contains("flash-lite"))
                            .ThenByDescending(m => m.Contains("3.5-flash"))
                            .ThenByDescending(m => m.Contains("3.6-flash"))
                            .ThenByDescending(m => m.Contains("flash"))
                            .ThenBy(m => m)
                            .ToList();
                    }
                }
            }
        }
        catch
        {
            // Fallback
        }

        return SupportedModels;
    }

    public async IAsyncEnumerable<AiStreamChunk> AnalyzeStreamAsync(
        AiAnalysisRequest request, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? apiKey = await _settingsService.GetApiKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            yield return new AiStreamChunk
            {
                IsComplete = true,
                ErrorMessage = "Gemini API key is not configured. Please open Settings (gear icon) and enter your Gemini API key."
            };
            yield break;
        }

        string rawModel = _settingsService.CurrentSettings?.SelectedModel ?? "gemini-3.5-flash-lite";
        string cleanModel = rawModel.Trim();
        if (cleanModel.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            cleanModel = cleanModel.Substring("models/".Length);
        }

        // Build candidate fallback list to ensure user never gets a 404 dead end
        var candidateModels = new List<string>();
        if (!string.IsNullOrWhiteSpace(cleanModel) && !cleanModel.Contains("1.5") && !cleanModel.Contains("2.5"))
        {
            candidateModels.Add(cleanModel);
        }

        var standardFallbacks = new[] { "gemini-3.5-flash-lite", "gemini-flash-lite-latest", "gemini-3.1-flash-lite", "gemini-3.5-flash", "gemini-3.6-flash" };
        foreach (var fb in standardFallbacks)
        {
            if (!candidateModels.Contains(fb, StringComparer.OrdinalIgnoreCase))
            {
                candidateModels.Add(fb);
            }
        }

        HttpResponseMessage? successfulResponse = null;
        string? lastError = null;

        var requestBody = BuildGeminiPayload(request);
        string payloadJson = requestBody.ToJsonString();

        foreach (var modelToTry in candidateModels)
        {
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelToTry}:streamGenerateContent?alt=sse&key={apiKey.Trim()}";
            var jsonContent = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = jsonContent };

            try
            {
                var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.IsSuccessStatusCode)
                {
                    successfulResponse = response;
                    // Auto-sync working model to settings
                    if (_settingsService.CurrentSettings != null && !string.Equals(_settingsService.CurrentSettings.SelectedModel, modelToTry, StringComparison.OrdinalIgnoreCase))
                    {
                        _settingsService.CurrentSettings.SelectedModel = modelToTry;
                        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings, CancellationToken.None);
                    }
                    break;
                }
                else
                {
                    string errorBody = await response.Content.ReadAsStringAsync(ct);
                    string? parsedMsg = null;
                    try
                    {
                        var errDoc = JsonNode.Parse(errorBody);
                        parsedMsg = errDoc?["error"]?["message"]?.ToString();
                    }
                    catch { }

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        lastError = "Gemini API rate limit or quota exceeded. Please wait a moment before retrying.";
                        break;
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        lastError = parsedMsg ?? "Gemini API key is invalid or unauthorized. Please verify your API key in Settings.";
                        break;
                    }
                    else
                    {
                        lastError = parsedMsg ?? $"Gemini API error ({response.StatusCode}): {errorBody}";
                    }
                }
            }
            catch (Exception ex)
            {
                lastError = $"Network connection error: {ex.Message}";
            }
        }

        if (successfulResponse == null)
        {
            yield return new AiStreamChunk
            {
                IsComplete = true,
                ErrorMessage = lastError ?? "Could not connect to Gemini API. Please check your API key in Settings."
            };
            yield break;
        }

        using var responseStream = await successfulResponse.Content.ReadAsStreamAsync(ct);
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

            if (line.StartsWith("data: "))
            {
                string jsonPayload = line.Substring(6).Trim();
                if (string.IsNullOrWhiteSpace(jsonPayload)) continue;

                string? chunkText = ExtractTextFromChunk(jsonPayload);
                if (!string.IsNullOrEmpty(chunkText))
                {
                    fullAccumulatedText.Append(chunkText);
                    streamBuffer.Append(chunkText);

                    // Check if JSON header is present and can be parsed
                    if (!headerParsed)
                    {
                        string currentTotal = streamBuffer.ToString();
                        int contentDelimiterIdx = currentTotal.IndexOf("---CONTENT---", StringComparison.Ordinal);

                        if (contentDelimiterIdx >= 0)
                        {
                            string jsonBlock = currentTotal.Substring(0, contentDelimiterIdx);
                            ParseHeaderJson(jsonBlock, out detectedIntent, out parsedTitle, out parsedSummary, out parsedActions);
                            headerParsed = true;

                            // Emit header info and the remainder of content
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
                        // Header already parsed, stream chunk directly
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
            }
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
            if (chunk.ErrorMessage != null)
            {
                error = chunk.ErrorMessage;
            }

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

    private static JsonObject BuildGeminiPayload(AiAnalysisRequest request)
    {
        var partsArray = new JsonArray();

        // 1. Inline Image Part (if available)
        if (request.Region.ImageBytes.Length > 0)
        {
            string base64Image = Convert.ToBase64String(request.Region.ImageBytes);
            partsArray.Add(new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = "image/jpeg",
                    ["data"] = base64Image
                }
            });
        }

        // 2. User Prompt Part
        string promptText = ScreenLensPrompts.BuildUserPrompt(request);
        partsArray.Add(new JsonObject
        {
            ["text"] = promptText
        });

        // 3. Contents Array with conversation history support
        var contentsArray = new JsonArray();

        foreach (var chat in request.ConversationHistory)
        {
            contentsArray.Add(new JsonObject
            {
                ["role"] = chat.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user" : "model",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = chat.Content } }
            });
        }

        // Current turn
        contentsArray.Add(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = partsArray
        });

        // Root JSON payload
        var root = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = ScreenLensPrompts.SystemInstruction }
                }
            },
            ["contents"] = contentsArray,
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0.2,
                ["topP"] = 0.95
            }
        };

        return root;
    }

    private static string? ExtractTextFromChunk(string sseJson)
    {
        try
        {
            var node = JsonNode.Parse(sseJson);
            var candidates = node?["candidates"]?.AsArray();
            if (candidates != null && candidates.Count > 0)
            {
                var parts = candidates[0]?["content"]?["parts"]?.AsArray();
                if (parts != null && parts.Count > 0)
                {
                    var textNode = parts[0]?["text"];
                    return textNode?.ToString();
                }
            }
        }
        catch
        {
            // Ignore malformed partial chunks
        }
        return null;
    }

    private static void ParseHeaderJson(
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
