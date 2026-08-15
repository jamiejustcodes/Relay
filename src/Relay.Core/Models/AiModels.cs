namespace Relay.Core.Models;

/// <summary>
/// A single turn in a multi-turn conversation.
/// </summary>
public record ChatMessage(string Role, string Content);

/// <summary>
/// Actionable item suggested by the AI based on intent.
/// </summary>
public record ActionItem(
    string Label,
    string ActionType,
    string? Payload = null,
    string? Icon = null
);

/// <summary>
/// Request payload sent to the AI provider.
/// </summary>
public record AiAnalysisRequest
{
    public required CaptureRegion Region { get; init; }
    public ScreenContext? Context { get; init; }
    public string? UserQuestion { get; init; }
    public IntentType? RequestedIntent { get; init; }
    public IReadOnlyList<ChatMessage> ConversationHistory { get; init; } = Array.Empty<ChatMessage>();
    public bool Stream { get; init; } = true;
    public string? SystemPromptOverride { get; init; }
}

/// <summary>
/// Complete structured response returned by an AI provider.
/// </summary>
public record AiAnalysisResult
{
    public IntentType DetectedIntent { get; init; } = IntentType.General;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string MarkdownContent { get; init; } = string.Empty;
    public IReadOnlyList<ActionItem> ActionItems { get; init; } = Array.Empty<ActionItem>();
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public Dictionary<string, string> Metadata { get; init; } = new();
    public string? RawResponse { get; init; }
    public TimeSpan ElapsedTime { get; init; }
}

/// <summary>
/// Incremental streaming chunk emitted by the AI provider.
/// </summary>
public record AiStreamChunk
{
    public string TextDelta { get; init; } = string.Empty;
    public bool IsComplete { get; init; }
    public IntentType? DetectedIntent { get; init; }
    public string? Title { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<ActionItem>? ActionItems { get; init; }
    public string? ErrorMessage { get; init; }
}
