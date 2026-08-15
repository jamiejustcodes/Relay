using System.Globalization;
using System.Windows;
using FluentAssertions;
using Moq;
using Relay.Core.Interfaces;
using Relay.Core.Models;
using Relay.UI.Controls;
using Relay.UI.ViewModels;
using Xunit;

namespace Relay.Tests;

public class HistoryViewModelAndConverterTests
{
    [Fact]
    public async Task HistoryViewModel_LoadAndSearch_ShouldUpdateItemsAndSelection()
    {
        var mockRepo = new Mock<IHistoryRepository>();
        var sampleItems = new List<HistoryItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Test Item 1",
                Summary = "Summary 1",
                MarkdownResponse = "Response 1",
                Intent = IntentType.Debug
            },
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Test Item 2",
                Summary = "Summary 2",
                MarkdownResponse = "Response 2",
                Intent = IntentType.Shop
            }
        };

        mockRepo.Setup(r => r.GetHistoryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleItems);

        var vm = new HistoryViewModel(mockRepo.Object);

        await vm.LoadHistoryAsync();

        vm.Items.Should().HaveCount(2);
        vm.HasItems.Should().BeTrue();
        vm.SelectedItem.Should().NotBeNull();
        vm.SelectedItem!.Title.Should().Be("Test Item 1");

        // Clear search
        await vm.ClearSearchAsync();
        vm.SearchFilter.Should().BeEmpty();
    }

    [Fact]
    public void NullToVisibilityConverter_ShouldConvertCorrectly()
    {
        var conv = new NullToVisibilityConverter();

        conv.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);

        conv.Convert("valid object", typeof(Visibility), null, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Visible);

        conv.Convert(null, typeof(Visibility), "Invert", CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Visible);

        conv.Convert("valid object", typeof(Visibility), "Invert", CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void BooleanToVisibilityConverter_ShouldSupportInvert()
    {
        var conv = new Relay.UI.Controls.BooleanToVisibilityConverter();

        conv.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Visible);

        conv.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);

        conv.Convert(true, typeof(Visibility), "Invert", CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);

        conv.Convert(false, typeof(Visibility), "Invert", CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Visible);
    }

    [Fact]
    public void CountToVisibilityConverter_ShouldHandleCountsAndInvert()
    {
        var conv = new CountToVisibilityConverter();

        conv.Convert(5, typeof(Visibility), null, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Visible);

        conv.Convert(0, typeof(Visibility), null, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);

        conv.Convert(0, typeof(Visibility), "Invert", CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Visible);
    }

    [Fact]
    public void HotkeyParser_ShouldParseControlShiftSpace()
    {
        var (modifiers, key) = Relay.Infrastructure.Hotkeys.HotkeyParser.Parse("Control + Shift", "Space");
        (modifiers & 0x0002).Should().Be(0x0002); // MOD_CONTROL
        (modifiers & 0x0004).Should().Be(0x0004); // MOD_SHIFT
        key.Should().Be(0x20); // VK_SPACE
    }

    [Fact]
    public async Task OverlayViewModel_PromptMode_ShouldCalculatePositionAndDeliverPrompt()
    {
        var captureMock = new Mock<IScreenCaptureService>();
        var contextMock = new Mock<IWindowContextService>();
        var ocrMock = new Mock<IOcrService>();

        captureMock.Setup(c => c.CaptureRegionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CaptureRegion { ImageBytes = new byte[10], Width = 200, Height = 100, DpiScale = 1.0 });
        contextMock.Setup(c => c.GetForegroundWindowContext())
            .Returns(new ScreenContext { ApplicationName = "VS Code", WindowTitle = "App.cs" });
        ocrMock.Setup(o => o.RecognizeTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("extracted code");

        var vm = new OverlayViewModel(captureMock.Object, contextMock.Object, ocrMock.Object);
        vm.IsPromptMode = true;
        vm.StartSelection(new Point(100, 100));
        vm.UpdateSelection(new Point(100, 100), new Point(400, 300));

        vm.SelectionWidth.Should().Be(300);
        vm.SelectionHeight.Should().Be(200);

        vm.CalculatePromptBarPosition(1920, 1080);
        vm.IsPromptBarVisible.Should().BeTrue();
        vm.PromptBarTop.Should().BeGreaterThan(300);

        string? deliveredPrompt = null;
        vm.SelectionCompleted += (s, args) =>
        {
            deliveredPrompt = args.Prompt;
        };

        await vm.ConfirmSelectionAsync(1.0, "Find the bug in this function");
        deliveredPrompt.Should().Be("Find the bug in this function");
    }
}
