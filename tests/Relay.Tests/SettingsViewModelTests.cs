using FluentAssertions;
using Moq;
using Relay.Core.Interfaces;
using Relay.Core.Models;
using Relay.UI.ViewModels;
using Xunit;

namespace Relay.Tests;

public class SettingsViewModelTests
{
    private static SettingsViewModel CreateViewModel(
        Mock<ISettingsService>? settingsMock = null,
        Mock<IAiProviderFactory>? aiFactoryMock = null)
    {
        var settingsService = settingsMock ?? new Mock<ISettingsService>();
        var aiFactory = aiFactoryMock ?? new Mock<IAiProviderFactory>();
        var hotkeyService = new Mock<IHotkeyService>();
        var startupService = new Mock<IStartupService>();
        var ollamaService = new Mock<IOllamaManagementService>();

        var geminiMock = new Mock<IAiProvider>();
        geminiMock.Setup(g => g.SupportedModels).Returns(new[] { "gemini-1.5-flash", "gemini-2.0-flash" });
        geminiMock.Setup(g => g.GetAvailableModelsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "gemini-1.5-flash", "gemini-2.0-flash" });
        aiFactory.Setup(f => f.GetProvider("gemini")).Returns(geminiMock.Object);

        ollamaService.Setup(o => o.GetRecommendedVisionModels()).Returns(new[] { "llava", "bakllava" });
        ollamaService.Setup(o => o.IsOllamaInstalled()).Returns(true);
        ollamaService.Setup(o => o.IsOllamaRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ollamaService.Setup(o => o.GetInstalledModelsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "llava" });

        return new SettingsViewModel(
            settingsService.Object,
            aiFactory.Object,
            hotkeyService.Object,
            startupService.Object,
            ollamaService.Object);
    }

    [Fact]
    public void SettingsViewModel_ShowApiKey_ShouldBeHiddenByDefault()
    {
        var vm = CreateViewModel();

        vm.ShowApiKey.Should().BeFalse();
        vm.ShowApiKeyTooltip.Should().Be("Show API key");
    }

    [Fact]
    public void SettingsViewModel_ToggleShowApiKey_ShouldFlipStateAndTooltip()
    {
        var vm = CreateViewModel();

        vm.ShowApiKey.Should().BeFalse();

        vm.ToggleShowApiKey();
        vm.ShowApiKey.Should().BeTrue();
        vm.ShowApiKeyTooltip.Should().Be("Hide API key");

        vm.ToggleShowApiKey();
        vm.ShowApiKey.Should().BeFalse();
        vm.ShowApiKeyTooltip.Should().Be("Show API key");
    }

    [Fact]
    public async Task SettingsViewModel_InitializeAsync_ShouldKeepShowApiKeyFalse()
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppSettings());
        settingsService.Setup(s => s.GetApiKeyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("AIzaSySecretApiKey123");

        var vm = CreateViewModel(settingsMock: settingsService);

        // Turn on before init
        vm.ShowApiKey = true;

        await vm.InitializeAsync();

        vm.ApiKey.Should().Be("AIzaSySecretApiKey123");
        vm.ShowApiKey.Should().BeFalse();
        vm.ShowApiKeyTooltip.Should().Be("Show API key");
    }
}
