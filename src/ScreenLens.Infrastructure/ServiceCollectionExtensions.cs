using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using ScreenLens.Core.Interfaces;
using ScreenLens.Infrastructure.Ai;
using ScreenLens.Infrastructure.Data;
using ScreenLens.Infrastructure.Hotkeys;
using ScreenLens.Infrastructure.Ocr;
using ScreenLens.Infrastructure.ScreenCapture;
using ScreenLens.Infrastructure.Search;
using ScreenLens.Infrastructure.Security;
using ScreenLens.Infrastructure.WindowContext;

namespace ScreenLens.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScreenLensInfrastructure(this IServiceCollection services)
    {
        // Security & Settings
        services.AddSingleton<ISecretVault, DpapiSecretVault>();
        services.AddSingleton<ISettingsService, SettingsService>();

        // System & Windows Interop
        services.AddSingleton<IScreenCaptureService, Win32ScreenCaptureService>();
        services.AddSingleton<IHotkeyService, Win32HotkeyService>();
        services.AddSingleton<IWindowContextService, Win32WindowContextService>();
        services.AddSingleton<IOcrService, WindowsMediaOcrService>();

        // Database & Storage
        services.AddSingleton<IHistoryRepository, SqliteHistoryRepository>();

        // HTTP & Search
        services.AddHttpClient<IAiProvider, GeminiAiProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = true
        });

        services.AddHttpClient<ISearchService, WebSearchService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }
}
