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
}
