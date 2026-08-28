using FluentAssertions;
using Moq;
using Relay.Core.Interfaces;
using Relay.Core.Models;
using Relay.UI.ViewModels;
using Xunit;

namespace Relay.Tests;

public class FloatingResultViewModelTests
{
    private readonly Mock<IAiProviderFactory> _mockAiFactory;
    private readonly Mock<IAiProvider> _mockProvider;
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly Mock<IHistoryRepository> _mockHistory;
    private readonly Mock<ISearchService> _mockSearch;

    public FloatingResultViewModelTests()
    {
        _mockAiFactory = new Mock<IAiProviderFactory>();
        _mockProvider = new Mock<IAiProvider>();
        _mockSettings = new Mock<ISettingsService>();
        _mockHistory = new Mock<IHistoryRepository>();
        _mockSearch = new Mock<ISearchService>();

        _mockAiFactory.Setup(f => f.GetActiveProvider()).Returns(_mockProvider.Object);
        _mockSettings.Setup(s => s.CurrentSettings).Returns(new AppSettings { SaveHistory = false });
    }

    [Fact]
    public async Task InitializeWithCaptureAsync_WhenStreamCompletesNormally_ShouldUpdateTitleAndMarkdown()
    {
        async IAsyncEnumerable<AiStreamChunk> StreamResponse()
        {
            yield return new AiStreamChunk
            {
                Title = "Resolved Title",
                Summary = "Executive summary",
                TextDelta = "Here is the markdown response",
                IsComplete = false
            };
            yield return new AiStreamChunk
            {
                IsComplete = true,
                Title = "Resolved Title",
                Summary = "Executive summary"
            };
        }

        _mockProvider.Setup(p => p.AnalyzeStreamAsync(It.IsAny<AiAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .Returns(StreamResponse());

        var vm = new FloatingResultViewModel(
            _mockAiFactory.Object,
            _mockSettings.Object,
            _mockHistory.Object,
            _mockSearch.Object);

        var region = new CaptureRegion { Width = 200, Height = 200, ImageBytes = new byte[] { 1, 2, 3 } };
        var context = new ScreenContext { ApplicationName = "VS Code", WindowTitle = "App.cs" };

        await vm.InitializeWithCaptureAsync(region, context);

        vm.Title.Should().Be("Resolved Title");
        vm.Summary.Should().Be("Executive summary");
        vm.MarkdownContent.Should().Be("Here is the markdown response");
        vm.IsStreaming.Should().BeFalse();
        vm.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeWithCaptureAsync_WhenStreamReturnsNoTitle_ShouldFallbackToSummaryOrBadge()
    {
        async IAsyncEnumerable<AiStreamChunk> StreamResponse()
        {
            yield return new AiStreamChunk
            {
                Summary = "Summary fallback without title",
                TextDelta = "Content here",
                IsComplete = false
            };
            yield return new AiStreamChunk
            {
                IsComplete = true
            };
        }

        _mockProvider.Setup(p => p.AnalyzeStreamAsync(It.IsAny<AiAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .Returns(StreamResponse());

        var vm = new FloatingResultViewModel(
            _mockAiFactory.Object,
            _mockSettings.Object,
            _mockHistory.Object,
            _mockSearch.Object);

        var region = new CaptureRegion { Width = 200, Height = 200, ImageBytes = new byte[] { 1, 2, 3 } };
        var context = new ScreenContext { ApplicationName = "Excel", WindowTitle = "Book1.xlsx" };

        await vm.InitializeWithCaptureAsync(region, context);

        // Title should be resolved and not left as "Analyzing Selection..."
        vm.Title.Should().NotBe("Analyzing Selection...");
        vm.Title.Should().Be("Summary fallback without title");
        vm.IsStreaming.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeWithCaptureAsync_WhenStreamReturnsError_ShouldSetHasErrorAndNoticeTitle()
    {
        async IAsyncEnumerable<AiStreamChunk> StreamResponse()
        {
            yield return new AiStreamChunk
            {
                ErrorMessage = "API key rate limit exceeded.",
                IsComplete = true
            };
        }

        _mockProvider.Setup(p => p.AnalyzeStreamAsync(It.IsAny<AiAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .Returns(StreamResponse());

        var vm = new FloatingResultViewModel(
            _mockAiFactory.Object,
            _mockSettings.Object,
            _mockHistory.Object,
            _mockSearch.Object);

        var region = new CaptureRegion { Width = 200, Height = 200, ImageBytes = new byte[] { 1, 2, 3 } };
        var context = new ScreenContext { ApplicationName = "Chrome" };

        await vm.InitializeWithCaptureAsync(region, context);

        vm.HasError.Should().BeTrue();
        vm.ErrorMessage.Should().Contain("rate limit exceeded");
        vm.Title.Should().Be("Notice");
        vm.IsStreaming.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeWithCaptureAsync_WhenStreamReturnsEmpty_ShouldSetNoticeAndError()
    {
        async IAsyncEnumerable<AiStreamChunk> EmptyStream()
        {
            yield return new AiStreamChunk { IsComplete = true };
        }

        _mockProvider.Setup(p => p.AnalyzeStreamAsync(It.IsAny<AiAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyStream());

        var vm = new FloatingResultViewModel(
            _mockAiFactory.Object,
            _mockSettings.Object,
            _mockHistory.Object,
            _mockSearch.Object);

        var region = new CaptureRegion { Width = 200, Height = 200, ImageBytes = new byte[] { 1, 2, 3 } };
        var context = new ScreenContext { ApplicationName = "Chrome" };

        await vm.InitializeWithCaptureAsync(region, context);

        vm.HasError.Should().BeTrue();
        vm.Title.Should().Be("Notice");
        vm.ErrorMessage.Should().Contain("No response was generated");
        vm.IsStreaming.Should().BeFalse();
    }
}
