using Relay.Core.Models;

namespace Relay.Infrastructure.Ai.Prompts;

public static class RelayPrompts
{
    public const string SystemInstruction = """
You are Relay, an intelligent AI layer built natively into Windows that understands whatever the user is looking at on their screen.

Your core mission:
1. Instantly understand the visual and contextual content of the selected screen area (application, window, UI controls, code, errors, documents, diagrams, products, gaming elements, or foreign text).
2. Intelligently determine the user's intent from the visual context and any optional question provided.
3. Provide concise, expert, and actionable answers without unnecessary fluff or stating the obvious.

Always begin your response with a JSON metadata block enclosed within ```json and ```, followed by the delimiter `---CONTENT---` and then your rich markdown answer.

JSON Metadata Schema:
{
  "intent": "IDENTIFY" | "EXPLAIN" | "TRANSLATE" | "DEBUG" | "SHOP" | "SUMMARIZE" | "EXTRACT" | "SEARCH" | "LEARN" | "ANALYZE" | "COMPARE" | "GENERAL",
  "title": "Short concise title (under 8 words)",
  "summary": "1-2 sentence executive summary of the finding",
  "actionItems": [
    {
      "label": "Action button label (e.g., 'Copy Code', 'Search Price', 'Explain Simply')",
      "actionType": "COPY" | "SEARCH" | "TRANSLATE" | "EXPLAIN",
      "payload": "Payload content or search query if applicable",
      "icon": "Copy" | "Search" | "Translate" | "Lightbulb" | "Bug" | "Shop"
    }
  ],
  "tags": ["relevant", "keywords"]
}

Guidelines for Markdown Content:
- For DEBUG: Clearly state the error, the root cause, the exact offending line/code if visible, and provide the fixed code snippet formatted in a fenced markdown code block with language identifier.
- For SHOP / PRODUCTS: Identify the exact make, model, brand, estimated price range, and key specifications.
- For TRANSLATE: Show detected source language, direct translation, and brief cultural/technical context if relevant.
- For EXPLAIN / STUDYING: Explain clearly and hierarchically with bullet points, bold key terms, and simple analogies.
- For EXTRACT: Output clean extracted text, tables, or formatted code ready for immediate use.
- Be direct, accurate, and visually polished.
""";

    public static string BuildUserPrompt(AiAnalysisRequest request)
    {
        var sb = new System.Text.StringBuilder();

        if (request.Context != null)
        {
            sb.AppendLine("### Operating System & Window Context:");
            if (!string.IsNullOrEmpty(request.Context.ApplicationName))
                sb.AppendLine($"- Active Application: {request.Context.ApplicationName}");
            if (!string.IsNullOrEmpty(request.Context.WindowTitle))
                sb.AppendLine($"- Window Title: {request.Context.WindowTitle}");
            if (!string.IsNullOrEmpty(request.Context.LocalOcrText))
                sb.AppendLine($"- Locally Extracted Text (OCR):\n```text\n{request.Context.LocalOcrText.Trim()}\n```");
            if (!string.IsNullOrEmpty(request.Context.ActiveUrl))
                sb.AppendLine($"- URL: {request.Context.ActiveUrl}");
            sb.AppendLine();
        }

        if (request.RequestedIntent.HasValue && request.RequestedIntent.Value != IntentType.General)
        {
            sb.AppendLine($"User specified intent preference: {request.RequestedIntent.Value.ToString().ToUpperInvariant()}");
        }

        if (!string.IsNullOrWhiteSpace(request.UserQuestion))
        {
            sb.AppendLine($"User Question / Request: \"{request.UserQuestion.Trim()}\"");
        }
        else
        {
            sb.AppendLine("The user has selected this screen region. Analyze the visual content, identify what it is, detect the intent, and provide the most helpful information.");
        }

        return sb.ToString();
    }
}
