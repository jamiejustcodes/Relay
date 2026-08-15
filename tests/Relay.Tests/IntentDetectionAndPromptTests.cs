using FluentAssertions;
using Relay.Core.Models;
using Relay.Infrastructure.Ai.Prompts;
using Xunit;

namespace Relay.Tests;

public class IntentDetectionAndPromptTests
{
    [Fact]
    public void BuildUserPrompt_ShouldIncludeContextAndOcrText()
    {
        var request = new AiAnalysisRequest
        {
            Region = new CaptureRegion { Width = 300, Height = 200, ImageBytes = new byte[] { 1, 2, 3 } },
            Context = new ScreenContext
            {
                ApplicationName = "Visual Studio",
                WindowTitle = "Program.cs - Relay",
                LocalOcrText = "NullReferenceException: Object reference not set to an instance of an object."
            },
            UserQuestion = "Why is this failing?"
        };

        string prompt = RelayPrompts.BuildUserPrompt(request);

        prompt.Should().Contain("Visual Studio");
        prompt.Should().Contain("Program.cs - Relay");
        prompt.Should().Contain("NullReferenceException");
        prompt.Should().Contain("Why is this failing?");
    }

    [Fact]
    public void BuildUserPrompt_WhenNoQuestionProvided_ShouldIncludeDefaultInstruction()
    {
        var request = new AiAnalysisRequest
        {
            Region = new CaptureRegion { Width = 200, Height = 200, ImageBytes = new byte[] { 1 } },
            Context = new ScreenContext
            {
                ApplicationName = "Google Chrome",
                WindowTitle = "Nike Air Max 95 - Shoes"
            }
        };

        string prompt = RelayPrompts.BuildUserPrompt(request);

        prompt.Should().Contain("Google Chrome");
        prompt.Should().Contain("Analyze the visual content, identify what it is, detect the intent");
    }

    [Theory]
    [InlineData("IDENTIFY", IntentType.Identify)]
    [InlineData("DEBUG", IntentType.Debug)]
    [InlineData("TRANSLATE", IntentType.Translate)]
    [InlineData("SHOP", IntentType.Shop)]
    [InlineData("EXPLAIN", IntentType.Explain)]
    [InlineData("SUMMARIZE", IntentType.Summarize)]
    [InlineData("EXTRACT", IntentType.Extract)]
    [InlineData("SEARCH", IntentType.Search)]
    public void IntentType_EnumParsing_ShouldMatchStringValues(string input, IntentType expected)
    {
        bool success = Enum.TryParse<IntentType>(input, true, out var result);
        success.Should().BeTrue();
        result.Should().Be(expected);
    }
}
