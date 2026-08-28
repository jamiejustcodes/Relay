using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Moq;
using Moq.Protected;
using Relay.Core.Interfaces;
using Relay.Core.Models;
using Relay.Infrastructure.Ai;
using Xunit;

namespace Relay.Tests;

public class GeminiProviderTests
{
    private readonly Mock<ISettingsService> _mockSettings;

    public GeminiProviderTests()
    {
        _mockSettings = new Mock<ISettingsService>();
        _mockSettings.Setup(s => s.GetApiKeyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("mock-api-key");
        _mockSettings.Setup(s => s.CurrentSettings)
            .Returns(new AppSettings { SelectedModel = "gemini-flash-latest" });
    }

    [Fact]
    public async Task ValidateCredentialsAsync_WhenApiKeyValid_ShouldReturnTrue()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"models\": []}")
            });

        var client = new HttpClient(handlerMock.Object);
        var provider = new GeminiAiProvider(client, _mockSettings.Object);

        bool result = await provider.ValidateCredentialsAsync("valid-key");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCredentialsAsync_WhenApiKeyEmpty_ShouldReturnFalse()
    {
        _mockSettings.Setup(s => s.GetApiKeyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var client = new HttpClient();
        var provider = new GeminiAiProvider(client, _mockSettings.Object);

        bool result = await provider.ValidateCredentialsAsync(null);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AnalyzeStreamAsync_WhenNoApiKeyConfigured_ShouldEmitErrorMessage()
    {
        _mockSettings.Setup(s => s.GetApiKeyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var client = new HttpClient();
        var provider = new GeminiAiProvider(client, _mockSettings.Object);

        var request = new AiAnalysisRequest
        {
            Region = new CaptureRegion { Width = 100, Height = 100, ImageBytes = new byte[] { 1, 2 } }
        };

        var chunks = new List<AiStreamChunk>();
        await foreach (var chunk in provider.AnalyzeStreamAsync(request))
        {
            chunks.Add(chunk);
        }

        chunks.Should().NotBeEmpty();
        chunks.First().ErrorMessage.Should().Contain("API key is not configured");
    }

    [Fact]
    public async Task AnalyzeStreamAsync_WithValidSseResponse_ShouldStreamMarkdownAndParseHeader()
    {
        string sseStream =
            "data: {\"candidates\": [{\"content\": {\"parts\": [{\"text\": \"```json\\n{\\n  \\\"intent\\\": \\\"DEBUG\\\",\\n  \\\"title\\\": \\\"NullReference Fix\\\",\\n  \\\"summary\\\": \\\"Object was null.\\\"\\n}\\n```\\n---CONTENT---\\n### Solution\\nCheck for null.\"}]}}]}\n\n" +
            "data: {\"candidates\": [{\"content\": {\"parts\": [{\"text\": \"\\nUse `if (obj != null)`.\"}]}}]}\n\n";

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(sseStream, Encoding.UTF8, "text/event-stream")
            });

        var client = new HttpClient(handlerMock.Object);
        var provider = new GeminiAiProvider(client, _mockSettings.Object);

        var request = new AiAnalysisRequest
        {
            Region = new CaptureRegion { Width = 100, Height = 100, ImageBytes = new byte[] { 1 } }
        };

        var result = await provider.AnalyzeAsync(request);

        result.DetectedIntent.Should().Be(IntentType.Debug);
        result.Title.Should().Be("NullReference Fix");
        result.Summary.Should().Be("Object was null.");
        result.MarkdownContent.Should().Contain("### Solution");
        result.MarkdownContent.Should().Contain("Use `if (obj != null)`");
    }

    [Fact]
    public async Task AnalyzeStreamAsync_WhenDelimiterMissing_ShouldStillParseHeaderAndStreamMarkdown()
    {
        // Model outputted ```json ... ``` but completely forgot the ---CONTENT--- delimiter
        string sseStream =
            "data: {\"candidates\": [{\"content\": {\"parts\": [{\"text\": \"```json\\n{\\n  \\\"intent\\\": \\\"EXPLAIN\\\",\\n  \\\"title\\\": \\\"UI Design Overview\\\",\\n  \\\"summary\\\": \\\"This is a dashboard view.\\\"\\n}\\n```\\n\\n### Detailed Breakdown\\nThis screen has a sidebar.\"}]}}]}\n\n" +
            "data: {\"candidates\": [{\"content\": {\"parts\": [{\"text\": \"\\nAnd a main content panel.\"}]}}]}\n\n";

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(sseStream, Encoding.UTF8, "text/event-stream")
            });

        var client = new HttpClient(handlerMock.Object);
        var provider = new GeminiAiProvider(client, _mockSettings.Object);

        var request = new AiAnalysisRequest
        {
            Region = new CaptureRegion { Width = 100, Height = 100, ImageBytes = new byte[] { 1 } }
        };

        var result = await provider.AnalyzeAsync(request);

        result.DetectedIntent.Should().Be(IntentType.Explain);
        result.Title.Should().Be("UI Design Overview");
        result.Summary.Should().Be("This is a dashboard view.");
        result.MarkdownContent.Should().Contain("### Detailed Breakdown");
        result.MarkdownContent.Should().Contain("And a main content panel.");
    }

    [Fact]
    public async Task AnalyzeStreamAsync_WhenDirectMarkdownWithoutJson_ShouldStreamAllMarkdown()
    {
        // Model skipped JSON header entirely and answered in markdown
        string sseStream =
            "data: {\"candidates\": [{\"content\": {\"parts\": [{\"text\": \"This is an image of the Windows Settings app where network configuration is displayed.\"}]}}]}\n\n" +
            "data: {\"candidates\": [{\"content\": {\"parts\": [{\"text\": \" To change your IP address, click on Properties.\"}]}}]}\n\n";

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(sseStream, Encoding.UTF8, "text/event-stream")
            });

        var client = new HttpClient(handlerMock.Object);
        var provider = new GeminiAiProvider(client, _mockSettings.Object);

        var request = new AiAnalysisRequest
        {
            Region = new CaptureRegion { Width = 100, Height = 100, ImageBytes = new byte[] { 1 } }
        };

        var result = await provider.AnalyzeAsync(request);

        result.MarkdownContent.Should().Contain("This is an image of the Windows Settings app");
        result.MarkdownContent.Should().Contain("To change your IP address, click on Properties.");
    }

    [Fact]
    public async Task AnalyzeStreamAsync_WhenDelimiterHasWhitespaceAndDifferentCase_ShouldParseSuccessfully()
    {
        // Model used `--- CONTENT ---` with spaces
        string sseStream =
            "data: {\"candidates\": [{\"content\": {\"parts\": [{\"text\": \"```json\\n{\\n  \\\"intent\\\": \\\"TRANSLATE\\\",\\n  \\\"title\\\": \\\"Japanese Text\\\",\\n  \\\"summary\\\": \\\"Welcome to our service\\\"\\n}\\n```\\n--- CONTENT ---\\n**Translation:** Welcome!\"}]}}]}\n\n";

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(sseStream, Encoding.UTF8, "text/event-stream")
            });

        var client = new HttpClient(handlerMock.Object);
        var provider = new GeminiAiProvider(client, _mockSettings.Object);

        var request = new AiAnalysisRequest
        {
            Region = new CaptureRegion { Width = 100, Height = 100, ImageBytes = new byte[] { 1 } }
        };

        var result = await provider.AnalyzeAsync(request);

        result.DetectedIntent.Should().Be(IntentType.Translate);
        result.Title.Should().Be("Japanese Text");
        result.Summary.Should().Be("Welcome to our service");
        result.MarkdownContent.Should().Contain("**Translation:** Welcome!");
    }

    [Fact]
    public async Task AnalyzeStreamAsync_WhenGeminiReturnsSafetyBlock_ShouldEmitErrorMessage()
    {
        string sseStream =
            "data: {\"candidates\": [{\"finishReason\": \"SAFETY\"}], \"promptFeedback\": {\"blockReason\": \"SAFETY\"}}\n\n";

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(sseStream, Encoding.UTF8, "text/event-stream")
            });

        var client = new HttpClient(handlerMock.Object);
        var provider = new GeminiAiProvider(client, _mockSettings.Object);

        var request = new AiAnalysisRequest
        {
            Region = new CaptureRegion { Width = 100, Height = 100, ImageBytes = new byte[] { 1 } }
        };

        var chunks = new List<AiStreamChunk>();
        await foreach (var chunk in provider.AnalyzeStreamAsync(request))
        {
            chunks.Add(chunk);
        }

        chunks.Should().Contain(c => c.ErrorMessage != null && c.ErrorMessage.Contains("safety"));
    }

    [Fact]
    public async Task AnalyzeStreamAsync_WhenGeminiReturnsInlineError_ShouldEmitErrorMessage()
    {
        string sseStream =
            "data: {\"error\": {\"code\": 429, \"message\": \"Resource has been exhausted (e.g. check quota).\"}}\n\n";

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(sseStream, Encoding.UTF8, "text/event-stream")
            });

        var client = new HttpClient(handlerMock.Object);
        var provider = new GeminiAiProvider(client, _mockSettings.Object);

        var request = new AiAnalysisRequest
        {
            Region = new CaptureRegion { Width = 100, Height = 100, ImageBytes = new byte[] { 1 } }
        };

        var chunks = new List<AiStreamChunk>();
        await foreach (var chunk in provider.AnalyzeStreamAsync(request))
        {
            chunks.Add(chunk);
        }

        chunks.Should().Contain(c => c.ErrorMessage != null && c.ErrorMessage.Contains("Resource has been exhausted"));
    }

    [Fact]
    public async Task AnalyzeStreamAsync_WithMultiplePartsInChunk_ShouldConcatenateText()
    {
        string sseStream =
            "data: {\"candidates\": [{\"content\": {\"parts\": [{\"thought\": true, \"text\": \"Thinking...\"}, {\"text\": \"```json\\n{\\\"title\\\": \\\"MultiPart Test\\\"}\\n```\\n---CONTENT---\\nHello \"}, {\"text\": \"World!\"}]}}]}\n\n";

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(sseStream, Encoding.UTF8, "text/event-stream")
            });

        var client = new HttpClient(handlerMock.Object);
        var provider = new GeminiAiProvider(client, _mockSettings.Object);

        var request = new AiAnalysisRequest
        {
            Region = new CaptureRegion { Width = 100, Height = 100, ImageBytes = new byte[] { 1 } }
        };

        var result = await provider.AnalyzeAsync(request);

        result.Title.Should().Be("MultiPart Test");
        result.MarkdownContent.Should().Contain("Hello World!");
    }
}
