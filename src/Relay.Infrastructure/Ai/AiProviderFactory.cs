using Relay.Core.Interfaces;

namespace Relay.Infrastructure.Ai;

/// <summary>
/// Resolves the active IAiProvider at runtime based on user settings.
/// </summary>
public class AiProviderFactory : IAiProviderFactory
{
    private readonly ISettingsService _settingsService;
    private readonly GeminiAiProvider _geminiProvider;
    private readonly OllamaAiProvider _ollamaProvider;

    public AiProviderFactory(
        ISettingsService settingsService,
        GeminiAiProvider geminiProvider,
        OllamaAiProvider ollamaProvider)
    {
        _settingsService = settingsService;
        _geminiProvider = geminiProvider;
        _ollamaProvider = ollamaProvider;
    }

    public IAiProvider GetActiveProvider()
    {
        string activeProvider = _settingsService.CurrentSettings?.ActiveProvider ?? "gemini";

        return activeProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            ? _ollamaProvider
            : _geminiProvider;
    }

    public IAiProvider GetProvider(string providerId)
    {
        return providerId.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            ? _ollamaProvider
            : _geminiProvider;
    }
}
