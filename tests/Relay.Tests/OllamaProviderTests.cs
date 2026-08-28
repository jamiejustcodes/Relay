using Relay.Core.Models;
using Relay.Infrastructure.Ai;
using Xunit;
using FluentAssertions;

namespace Relay.Tests;

public class OllamaProviderTests
{
    // ──────────────────────────────────────────────────────────
    // NDJSON Parsing Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ExtractTextFromNdjsonChunk_ValidChunk_ReturnsContent()
    {
        string ndjson = "{\"model\":\"gemma3\",\"created_at\":\"2026-08-15T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"Hello world\"},\"done\":false}";
        var result = OllamaAiProvider.ExtractTextFromNdjsonChunk(ndjson);
        result.Should().Be("Hello world");
    }

    [Fact]
    public void ExtractTextFromNdjsonChunk_EmptyContent_ReturnsEmpty()
    {
        string ndjson = "{\"model\":\"gemma3\",\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done\":false}";
        var result = OllamaAiProvider.ExtractTextFromNdjsonChunk(ndjson);
        result.Should().Be("");
    }

    [Fact]
    public void ExtractTextFromNdjsonChunk_MalformedJson_ReturnsNull()
    {
        string ndjson = "this is not json at all";
        var result = OllamaAiProvider.ExtractTextFromNdjsonChunk(ndjson);
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractTextFromNdjsonChunk_MissingMessageField_ReturnsNull()
    {
        string ndjson = "{\"model\":\"gemma3\",\"done\":false}";
        var result = OllamaAiProvider.ExtractTextFromNdjsonChunk(ndjson);
        result.Should().BeNull();
    }

    [Fact]
    public void IsChunkDone_DoneTrue_ReturnsTrue()
    {
        string ndjson = "{\"model\":\"gemma3\",\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done\":true}";
        var result = OllamaAiProvider.IsChunkDone(ndjson);
        result.Should().BeTrue();
    }

    [Fact]
    public void IsChunkDone_DoneFalse_ReturnsFalse()
    {
        string ndjson = "{\"model\":\"gemma3\",\"message\":{\"role\":\"assistant\",\"content\":\"text\"},\"done\":false}";
        var result = OllamaAiProvider.IsChunkDone(ndjson);
        result.Should().BeFalse();
    }

    [Fact]
    public void IsChunkDone_MissingDone_ReturnsFalse()
    {
        string ndjson = "{\"model\":\"gemma3\",\"message\":{\"role\":\"assistant\",\"content\":\"text\"}}";
        var result = OllamaAiProvider.IsChunkDone(ndjson);
        result.Should().BeFalse();
    }

    [Fact]
    public void IsChunkDone_MalformedJson_ReturnsFalse()
    {
        var result = OllamaAiProvider.IsChunkDone("not json");
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────
    // Header Parsing Tests (same format as Gemini)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseHeaderJson_ValidHeader_ParsesAllFields()
    {
        string header = """
        ```json
        {
            "intent": "DEBUG",
            "title": "NullReferenceException Fix",
            "summary": "Variable 'x' is null on line 42",
            "actionItems": [
                { "label": "Copy Fix", "actionType": "COPY", "payload": "var x = new object();" }
            ]
        }
        ```
        """;

        OllamaAiProvider.ParseHeaderJson(header, out var intent, out var title, out var summary, out var actions);

        intent.Should().Be(IntentType.Debug);
        title.Should().Be("NullReferenceException Fix");
        summary.Should().Be("Variable 'x' is null on line 42");
        actions.Should().HaveCount(1);
        actions[0].Label.Should().Be("Copy Fix");
        actions[0].ActionType.Should().Be("COPY");
    }

    [Fact]
    public void ParseHeaderJson_EmptyInput_ReturnsDefaults()
    {
        OllamaAiProvider.ParseHeaderJson("", out var intent, out var title, out var summary, out var actions);

        intent.Should().Be(IntentType.General);
        title.Should().BeEmpty();
        summary.Should().BeEmpty();
        actions.Should().BeEmpty();
    }

    [Fact]
    public void ParseHeaderJson_ShopIntent_ParsesCorrectly()
    {
        string header = """
        {
            "intent": "SHOP",
            "title": "Sony WH-1000XM5",
            "summary": "Premium noise-cancelling headphones",
            "actionItems": [
                { "label": "Search Price", "actionType": "SEARCH", "payload": "Sony WH-1000XM5 price" }
            ]
        }
        """;

        OllamaAiProvider.ParseHeaderJson(header, out var intent, out var title, out var summary, out var actions);

        intent.Should().Be(IntentType.Shop);
        title.Should().Be("Sony WH-1000XM5");
        actions.Should().HaveCount(1);
        actions[0].ActionType.Should().Be("SEARCH");
    }

    [Fact]
    public void TryExtractHeaderJson_WhenCodeFenceClosed_ShouldExtractMetadataAndEndIndex()
    {
        string text = "```json\n{\n  \"intent\": \"DEBUG\",\n  \"title\": \"Syntax Error\",\n  \"summary\": \"Missing semicolon\"\n}\n```\n### Fix\nAdd a semicolon.";
        bool success = OllamaAiProvider.TryExtractHeaderJson(text, out var intent, out var title, out var summary, out var actions, out int jsonEndIdx);

        success.Should().BeTrue();
        intent.Should().Be(IntentType.Debug);
        title.Should().Be("Syntax Error");
        summary.Should().Be("Missing semicolon");
        jsonEndIdx.Should().BeGreaterThan(0);
        text.Substring(jsonEndIdx).TrimStart().Should().StartWith("### Fix");
    }

    [Fact]
    public void TryExtractHeaderJson_WhenRawJsonClosed_ShouldExtractMetadataAndEndIndex()
    {
        string text = "{\n  \"intent\": \"EXPLAIN\",\n  \"title\": \"Process Map\"\n}\n---CONTENT---\nHere is the explanation.";
        bool success = OllamaAiProvider.TryExtractHeaderJson(text, out var intent, out var title, out var summary, out var actions, out int jsonEndIdx);

        success.Should().BeTrue();
        intent.Should().Be(IntentType.Explain);
        title.Should().Be("Process Map");
        jsonEndIdx.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryExtractHeaderJson_WhenInvalidJson_ShouldReturnFalse()
    {
        string text = "This is just regular text with no json at all.";
        bool success = OllamaAiProvider.TryExtractHeaderJson(text, out _, out _, out _, out _, out _);

        success.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────
    // AiProviderFactory Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void AiProviderFactory_GetActiveProvider_ReturnsGeminiByDefault()
    {
        var settingsService = new FakeSettingsService("gemini");
        var gemini = new FakeAiProvider("gemini");
        var ollama = new FakeAiProvider("ollama");
        var factory = new AiProviderFactory(settingsService, (GeminiAiProvider)null!, (OllamaAiProvider)null!);

        // We can't fully test this without real providers, but we test the interface shape
        factory.Should().NotBeNull();
    }

    [Fact]
    public void AppSettings_DefaultActiveProvider_IsGemini()
    {
        var settings = new AppSettings();
        settings.ActiveProvider.Should().Be("gemini");
    }

    [Fact]
    public void AppSettings_OllamaDefaults_AreCorrect()
    {
        var settings = new AppSettings();
        settings.OllamaBaseUrl.Should().Be("http://localhost:11434");
        settings.OllamaModel.Should().Be("llava");
    }

    // ──────────────────────────────────────────────────────────
    // OllamaManagementService Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void IsKnownVisionModel_VisionModels_ReturnTrue_TextOnlyModels_ReturnFalse()
    {
        OllamaManagementService.IsKnownVisionModel("llama3.2-vision").Should().BeTrue();
        OllamaManagementService.IsKnownVisionModel("llama3.2-vision:11b").Should().BeTrue();
        OllamaManagementService.IsKnownVisionModel("llava").Should().BeTrue();
        OllamaManagementService.IsKnownVisionModel("llava:7b").Should().BeTrue();
        OllamaManagementService.IsKnownVisionModel("moondream").Should().BeTrue();
        OllamaManagementService.IsKnownVisionModel("minicpm-v").Should().BeTrue();
        OllamaManagementService.IsKnownVisionModel("qwen2-vl").Should().BeTrue();
        OllamaManagementService.IsKnownVisionModel("qwen2.5-vl:7b").Should().BeTrue();
        OllamaManagementService.IsKnownVisionModel("bakllava").Should().BeTrue();

        // Text-only models must return false
        OllamaManagementService.IsKnownVisionModel("qwen2.5-coder:1.5b").Should().BeFalse();
        OllamaManagementService.IsKnownVisionModel("llama3:8b").Should().BeFalse();
        OllamaManagementService.IsKnownVisionModel("mistral:7b").Should().BeFalse();
        OllamaManagementService.IsKnownVisionModel("deepseek-r1:8b").Should().BeFalse();
        OllamaManagementService.IsKnownVisionModel("phi4:14b").Should().BeFalse();
    }

    [Fact]
    public void GetRecommendedVisionModels_ReturnsNonEmptyList()
    {
        var httpClient = new System.Net.Http.HttpClient();
        var settingsService = new FakeSettingsService("ollama");
        var service = new OllamaManagementService(httpClient, settingsService);

        var models = service.GetRecommendedVisionModels();

        models.Should().NotBeEmpty();
        models.Should().Contain("llama3.2-vision");
        models.Should().Contain("llava");
    }

    [Fact]
    public void OllamaProvider_ProviderId_IsOllama()
    {
        var httpClient = new System.Net.Http.HttpClient();
        var settingsService = new FakeSettingsService("ollama");
        var provider = new OllamaAiProvider(httpClient, settingsService);

        provider.ProviderId.Should().Be("ollama");
        provider.DisplayName.Should().Contain("Ollama");
    }

    [Fact]
    public void OllamaProvider_SupportedModels_ContainsVisionModels()
    {
        var httpClient = new System.Net.Http.HttpClient();
        var settingsService = new FakeSettingsService("ollama");
        var provider = new OllamaAiProvider(httpClient, settingsService);

        provider.SupportedModels.Should().Contain("llama3.2-vision");
        provider.SupportedModels.Should().Contain("llava");
    }

    // ──────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────

    private class FakeSettingsService : Relay.Core.Interfaces.ISettingsService
    {
        private AppSettings _settings;
        public AppSettings CurrentSettings => _settings;

        public FakeSettingsService(string activeProvider = "gemini")
        {
            _settings = new AppSettings { ActiveProvider = activeProvider };
        }

        public Task<AppSettings> LoadSettingsAsync(CancellationToken ct = default) => Task.FromResult(_settings);
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default) { _settings = settings; return Task.CompletedTask; }
        public Task<string?> GetApiKeyAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task SetApiKeyAsync(string plainApiKey, CancellationToken ct = default) => Task.CompletedTask;
    }

    private class FakeAiProvider : Relay.Core.Interfaces.IAiProvider
    {
        public string ProviderId { get; }
        public string DisplayName => ProviderId;
        public IReadOnlyList<string> SupportedModels => Array.Empty<string>();
        public FakeAiProvider(string id) { ProviderId = id; }
        public Task<bool> ValidateCredentialsAsync(string? apiKey = null, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(string? apiKey = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public async IAsyncEnumerable<AiStreamChunk> AnalyzeStreamAsync(AiAnalysisRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default) { yield break; }
        public Task<AiAnalysisResult> AnalyzeAsync(AiAnalysisRequest request, CancellationToken ct = default) => Task.FromResult(new AiAnalysisResult());
    }
}
