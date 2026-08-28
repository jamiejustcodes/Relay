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

public class GeminiAiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;

    public string ProviderId => "gemini";
    public string DisplayName => "Google Gemini";
    public IReadOnlyList<string> SupportedModels => new[]
    {
        "gemini-2.5-flash",
        "gemini-2.0-flash",
        "gemini-1.5-flash",
        "gemini-flash-latest"
    };

    private static readonly System.Text.RegularExpressions.Regex DelimiterRegex =
        new(@"---+\s*content\s*---+", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

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

                            // Filter out deprecated, text-only legacy, or specialized non-chat endpoints
                            bool isDeprecatedOrSpecialized = name.Contains("embedding", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Contains("aqa", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Contains("imagen", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Contains("tts", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Contains("robotics", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Contains("computer-use", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Contains("video-understanding", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Contains("customtools", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Contains("clip", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Contains("gemma", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Contains("learnlm", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Equals("gemini-pro", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Equals("gemini-1.0-pro", StringComparison.OrdinalIgnoreCase) ||
                                                           name.Equals("gemini-pro-vision", StringComparison.OrdinalIgnoreCase);

                            if (!isDeprecatedOrSpecialized && name.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
                            {
                                result.Add(name);
                            }
                        }
                    }

                    if (result.Count > 0)
                    {
                        return result
                            .OrderByDescending(m => m.Equals("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase))
                            .ThenByDescending(m => m.Equals("gemini-2.0-flash", StringComparison.OrdinalIgnoreCase))
                            .ThenByDescending(m => m.Equals("gemini-1.5-flash", StringComparison.OrdinalIgnoreCase))
                            .ThenByDescending(m => m.Equals("gemini-flash-latest", StringComparison.OrdinalIgnoreCase))
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

        string rawModel = _settingsService.CurrentSettings?.SelectedModel ?? "gemini-2.5-flash";
        string cleanModel = rawModel.Trim();
        if (cleanModel.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            cleanModel = cleanModel.Substring("models/".Length);
        }

        // Build candidate fallback list to ensure user never gets a dead end
        var candidateModels = new List<string>();
        if (!string.IsNullOrWhiteSpace(cleanModel) &&
            !cleanModel.Equals("gemini-pro", StringComparison.OrdinalIgnoreCase) &&
            !cleanModel.Equals("gemini-1.0-pro", StringComparison.OrdinalIgnoreCase) &&
            !cleanModel.Equals("gemini-pro-latest", StringComparison.OrdinalIgnoreCase))
        {
            candidateModels.Add(cleanModel);
        }

        var standardFallbacks = new[]
        {
            "gemini-2.5-flash",
            "gemini-2.0-flash",
            "gemini-1.5-flash",
            "gemini-flash-latest"
        };

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
            if (ct.IsCancellationRequested) break;

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

                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        lastError = parsedMsg ?? "Gemini API key is invalid or unauthorized. Please verify your API key in Settings.";
                        break;
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        lastError = parsedMsg ?? $"Model '{modelToTry}' request failed. Trying fallback model...";
                        continue;
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        lastError = $"Model '{modelToTry}' is no longer available. Trying fallback model...";
                        continue;
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        lastError = parsedMsg ?? "Gemini API rate limit exceeded on this model. Trying fallback model...";
                        continue;
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
                break;
            }
        }

        if (successfulResponse == null)
        {
            yield return new AiStreamChunk
            {
                IsComplete = true,
                ErrorMessage = lastError ?? "Could not connect to Gemini API. Please check your API key and model selection in Settings."
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

        using var readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        while (!ct.IsCancellationRequested)
        {
            string? line = null;
            bool readTimedOut = false;
            try
            {
                readTimeoutCts.CancelAfter(TimeSpan.FromSeconds(35));
                line = await reader.ReadLineAsync(readTimeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                readTimedOut = true;
            }

            if (readTimedOut)
            {
                yield return new AiStreamChunk
                {
                    IsComplete = true,
                    ErrorMessage = "Streaming response from Gemini timed out."
                };
                yield break;
            }

            if (line == null) break;

            if (line.StartsWith("data: "))
            {
                string jsonPayload = line.Substring(6).Trim();
                if (string.IsNullOrWhiteSpace(jsonPayload)) continue;

                var (chunkText, streamError) = ProcessSseJsonChunk(jsonPayload);
                if (!string.IsNullOrEmpty(streamError))
                {
                    yield return new AiStreamChunk
                    {
                        IsComplete = true,
                        ErrorMessage = streamError
                    };
                    yield break;
                }

                if (!string.IsNullOrEmpty(chunkText))
                {
                    fullAccumulatedText.Append(chunkText);

                    if (headerParsed)
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
                    else
                    {
                        streamBuffer.Append(chunkText);
                        string currentTotal = streamBuffer.ToString();

                        // 1. Try delimiter match (e.g. ---CONTENT---, --- CONTENT ---)
                        var delimiterMatch = DelimiterRegex.Match(currentTotal);
                        if (delimiterMatch.Success)
                        {
                            string headerPart = currentTotal.Substring(0, delimiterMatch.Index);
                            TryExtractHeaderJson(headerPart, out detectedIntent, out parsedTitle, out parsedSummary, out parsedActions, out _);
                            headerParsed = true;

                            string remainingContent = currentTotal.Substring(delimiterMatch.Index + delimiterMatch.Length).TrimStart('\r', '\n');
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
                        // 2. Try closed JSON code fence or raw JSON block
                        else if (TryExtractHeaderJson(currentTotal, out var pIntent, out var pTitle, out var pSummary, out var pActions, out int jsonEndIdx))
                        {
                            detectedIntent = pIntent;
                            parsedTitle = pTitle;
                            parsedSummary = pSummary;
                            parsedActions = pActions;
                            headerParsed = true;

                            string remainingContent = currentTotal.Substring(jsonEndIdx).TrimStart();
                            if (DelimiterRegex.IsMatch(remainingContent))
                            {
                                remainingContent = DelimiterRegex.Replace(remainingContent, "", 1).TrimStart('\r', '\n');
                            }
                            else if (remainingContent.StartsWith("---", StringComparison.Ordinal))
                            {
                                remainingContent = remainingContent.TrimStart('-').TrimStart();
                            }

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
                        // 3. Fallback: If buffer has grown significantly without any JSON header markers, start streaming directly
                        else if (currentTotal.Length > 80 &&
                                 !currentTotal.TrimStart().StartsWith('{') &&
                                 !currentTotal.TrimStart().StartsWith("```") &&
                                 !currentTotal.Contains("```json", StringComparison.OrdinalIgnoreCase))
                        {
                            headerParsed = true;
                            streamBuffer.Clear();

                            yield return new AiStreamChunk
                            {
                                TextDelta = currentTotal,
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
        }

        // Completion & un-emitted buffer flush guarantee
        if (!headerParsed)
        {
            string full = fullAccumulatedText.ToString();
            if (TryExtractHeaderJson(full, out var finIntent, out var finTitle, out var finSummary, out var finActions, out int finEndIdx))
            {
                detectedIntent = finIntent;
                parsedTitle = finTitle;
                parsedSummary = finSummary;
                parsedActions = finActions;

                string remaining = full.Substring(finEndIdx).TrimStart();
                if (DelimiterRegex.IsMatch(remaining))
                {
                    remaining = DelimiterRegex.Replace(remaining, "", 1).TrimStart('\r', '\n');
                }
                else if (remaining.StartsWith("---", StringComparison.Ordinal))
                {
                    remaining = remaining.TrimStart('-').TrimStart();
                }

                if (!string.IsNullOrEmpty(remaining))
                {
                    yield return new AiStreamChunk
                    {
                        TextDelta = remaining,
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
                // No JSON header at all in response — emit the entire full text as markdown content
                if (!string.IsNullOrEmpty(full))
                {
                    yield return new AiStreamChunk
                    {
                        TextDelta = full,
                        DetectedIntent = detectedIntent,
                        Title = parsedTitle,
                        Summary = parsedSummary,
                        ActionItems = parsedActions,
                        IsComplete = false
                    };
                }
            }
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
        string promptText = RelayPrompts.BuildUserPrompt(request);
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
                    new JsonObject { ["text"] = RelayPrompts.SystemInstruction }
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

    private static (string? Text, string? ErrorMessage) ProcessSseJsonChunk(string sseJson)
    {
        try
        {
            var node = JsonNode.Parse(sseJson);
            if (node == null) return (null, null);

            // 1. Check for API error node
            var errorNode = node["error"];
            if (errorNode != null)
            {
                string errorMsg = errorNode["message"]?.ToString() ?? "Gemini API error during stream generation.";
                return (null, errorMsg);
            }

            // 2. Check for prompt safety blocks
            var blockReason = node["promptFeedback"]?["blockReason"]?.ToString();
            if (!string.IsNullOrEmpty(blockReason))
            {
                return (null, $"Gemini analysis was blocked by safety filters ({blockReason}).");
            }

            // 3. Check candidates & candidate finish reasons
            var candidates = node["candidates"]?.AsArray();
            if (candidates != null && candidates.Count > 0)
            {
                var firstCandidate = candidates[0];
                var finishReason = firstCandidate?["finishReason"]?.ToString();
                if (finishReason == "SAFETY" || finishReason == "RECITATION" || finishReason == "BLOCKLIST" || finishReason == "PROHIBITED_CONTENT" || finishReason == "SPII")
                {
                    return (null, $"Gemini analysis could not be completed due to safety policy ({finishReason}).");
                }

                var parts = firstCandidate?["content"]?["parts"]?.AsArray();
                if (parts != null)
                {
                    var sbPart = new StringBuilder();
                    foreach (var part in parts)
                    {
                        bool isThought = part?["thought"]?.GetValue<bool>() ?? false;
                        if (!isThought)
                        {
                            var text = part?["text"]?.ToString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                sbPart.Append(text);
                            }
                        }
                    }
                    if (sbPart.Length > 0)
                    {
                        return (sbPart.ToString(), null);
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed partial chunks
        }
        return (null, null);
    }

    internal static bool TryExtractHeaderJson(
        string text,
        out IntentType intent,
        out string title,
        out string summary,
        out List<ActionItem> actions,
        out int jsonEndIndex)
    {
        intent = IntentType.General;
        title = string.Empty;
        summary = string.Empty;
        actions = new List<ActionItem>();
        jsonEndIndex = -1;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        int startIdx = text.IndexOf('{');
        if (startIdx < 0) return false;

        // Track balanced braces
        int openBraces = 0;
        int closeIdx = -1;
        bool inString = false;
        bool isEscaped = false;

        for (int i = startIdx; i < text.Length; i++)
        {
            char c = text[i];
            if (isEscaped)
            {
                isEscaped = false;
                continue;
            }

            if (c == '\\')
            {
                isEscaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (!inString)
            {
                if (c == '{')
                {
                    openBraces++;
                }
                else if (c == '}')
                {
                    openBraces--;
                    if (openBraces == 0)
                    {
                        closeIdx = i;
                        break;
                    }
                }
            }
        }

        if (closeIdx > startIdx)
        {
            string candidateJson = text.Substring(startIdx, closeIdx - startIdx + 1);
            try
            {
                using var doc = JsonDocument.Parse(candidateJson);
                var root = doc.RootElement;

                bool hasIntent = root.TryGetProperty("intent", out var intentProp);
                bool hasTitle = root.TryGetProperty("title", out var titleProp);
                bool hasSummary = root.TryGetProperty("summary", out var summaryProp);

                if (hasIntent || hasTitle || hasSummary)
                {
                    if (hasIntent)
                    {
                        string intentStr = intentProp.GetString() ?? "";
                        if (Enum.TryParse<IntentType>(intentStr, true, out var parsed))
                        {
                            intent = parsed;
                        }
                    }

                    if (hasTitle)
                    {
                        title = titleProp.GetString() ?? "";
                    }

                    if (hasSummary)
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

                    jsonEndIndex = closeIdx + 1;
                    int nextCodeFence = text.IndexOf("```", jsonEndIndex, StringComparison.Ordinal);
                    if (nextCodeFence >= 0 && nextCodeFence <= jsonEndIndex + 10)
                    {
                        jsonEndIndex = nextCodeFence + 3;
                    }
                    return true;
                }
            }
            catch
            {
                // Not valid JSON
            }
        }

        return false;
    }

    internal static void ParseHeaderJson(
        string rawHeader, 
        out IntentType intent, 
        out string title, 
        out string summary, 
        out List<ActionItem> actions)
    {
        TryExtractHeaderJson(rawHeader, out intent, out title, out summary, out actions, out _);
    }
}
