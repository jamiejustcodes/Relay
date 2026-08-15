using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Relay.Core.Interfaces;
using Relay.Infrastructure.Ai;
using Relay.Infrastructure.Data;
using Relay.Infrastructure.Hotkeys;
using Relay.Infrastructure.Ocr;
using Relay.Infrastructure.ScreenCapture;
using Relay.Infrastructure.Search;
using Relay.Infrastructure.Security;
using Relay.Infrastructure.Startup;
using Relay.Infrastructure.WindowContext;

namespace Relay.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRelayInfrastructure(this IServiceCollection services)
    {
        // Security & Settings
        services.AddSingleton<ISecretVault, DpapiSecretVault>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IStartupService, WindowsStartupService>();

        // System & Windows Interop
        services.AddSingleton<IScreenCaptureService, Win32ScreenCaptureService>();
        services.AddSingleton<IHotkeyService, Win32HotkeyService>();
        services.AddSingleton<IWindowContextService, Win32WindowContextService>();
        services.AddSingleton<IOcrService, WindowsMediaOcrService>();

        // Database & Storage
        services.AddSingleton<IHistoryRepository, SqliteHistoryRepository>();

        // HTTP & AI Providers
        var socketHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = true
        };

        // Gemini provider with dedicated HttpClient
        services.AddHttpClient<GeminiAiProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = true
        });

        // Ollama provider with its own HttpClient (longer timeout for local inference)
        services.AddHttpClient<OllamaAiProvider>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        });

        // Ollama management service (install, pull, status)
        services.AddHttpClient<OllamaManagementService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(30); // Large model downloads can take time
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        });
        services.AddSingleton<IOllamaManagementService>(sp => sp.GetRequiredService<OllamaManagementService>());

        // AI Provider Factory — resolves active provider at runtime
        services.AddSingleton<AiProviderFactory>();
        services.AddSingleton<IAiProviderFactory>(sp => sp.GetRequiredService<AiProviderFactory>());

        // IAiProvider resolves to the factory's active provider for backwards compatibility
        services.AddSingleton<IAiProvider>(sp => sp.GetRequiredService<AiProviderFactory>().GetActiveProvider());

        // Web Search
        services.AddHttpClient<ISearchService, WebSearchService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }
}

