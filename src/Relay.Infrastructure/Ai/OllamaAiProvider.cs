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

        using var readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        while (!ct.IsCancellationRequested)
        {
            string? line = null;
            bool readTimedOut = false;
            try
            {
                readTimeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
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
                    ErrorMessage = "Streaming response from Ollama timed out."
                };
                yield break;
            }

            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Each line is a complete JSON object (NDJSON)
            string? chunkText = ExtractTextFromNdjsonChunk(line);
            bool isDone = IsChunkDone(line);

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

            if (isDone) break;
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

    private static readonly System.Text.RegularExpressions.Regex DelimiterRegex =
        new(@"---+\s*content\s*---+", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

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
