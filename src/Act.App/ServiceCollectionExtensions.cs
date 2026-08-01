using Act.Agents.ClaudeCode;
using Act.Agents.Codex;
using Act.App.Cards;
using Act.App.Desktop;
using Act.App.Sessions;
using Act.App.Settings;
using Act.App.Usage;
using Act.Core.Abstractions;
using Act.Infrastructure;
using Act.Infrastructure.Storage;
using Act.Infrastructure.Usage;
using ElectronNET.API;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Radzen;

namespace Act.App;

public static class ServiceCollectionExtensions
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddActApp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddRadzenComponents();
        services.AddSingleton<IAssetVersions, AssetVersions>();
        services.AddScoped<IDesktopBridge, BrowserDesktopBridge>();
        services.AddActInfrastructure(
            ActDataDirectory.Resolve(configuration[ActDataDirectory.OverrideKey]));

        return services
            .AddActAgents()
            .AddActBoard()
            .AddActSessions()
            .AddActUsage(configuration);
    }

    // Electron-only, and registered from the branch that decides the app runs as a desktop shell:
    // resolving DesktopShell without Electron's own services behind it would fail.
    public static IServiceCollection AddActDesktopShell(this IServiceCollection services)
    {
        services.AddElectron();
        services.AddSingleton<DesktopShell>();
        services.Replace(ServiceDescriptor.Scoped<IDesktopBridge, ElectronDesktopBridge>());

        return services;
    }

    private static IServiceCollection AddActAgents(this IServiceCollection services)
    {
        services.AddSingleton<IAgentAdapter, ClaudeCodeAdapter>();
        services.AddSingleton<IAgentAdapter, CodexAdapter>();
        services.AddSingleton<IHookNormalizer, ClaudeCodeHookNormalizer>();
        services.AddSingleton<IHookNormalizer, CodexHookNormalizer>();
        services.AddSingleton<ITranscriptNormalizer, ClaudeCodeTranscriptNormalizer>();
        services.AddSingleton<ITranscriptNormalizer, CodexTranscriptNormalizer>();
        services.AddSingleton<IAgentCapabilityCatalog, AgentCapabilityCatalog>();

        return services;
    }

    private static IServiceCollection AddActBoard(this IServiceCollection services)
    {
        services.AddSingleton<AppCulture>();
        services.AddSingleton<UserSettingsService>();
        services.AddSingleton<AgentDefaultsMigration>();
        services.AddSingleton<AgentInstallDiscovery>();
        services.AddSingleton<BoardState>();
        services.AddSingleton<RetentionPump>();
        services.AddSingleton<CardCompleter>();

        return services;
    }

    private static IServiceCollection AddActUsage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(
            configuration.GetSection(UsageOptions.SectionName).Get<UsageOptions>() ?? new UsageOptions());

        services.AddHttpClient(HttpUsageProbe.ClientName, client => client.Timeout = RequestTimeout);

        services.AddSingleton<IUsageProbe>(provider => Probe(provider, new ClaudeCodeUsageDialect()));
        services.AddSingleton<IUsageProbe>(provider => Probe(provider, new CodexUsageDialect()));

        services.AddSingleton<UsageState>();
        services.AddSingleton<UsagePump>();

        return services;
    }

    private static HttpUsageProbe Probe(IServiceProvider provider, IUsageDialect dialect)
        => new(
            dialect,
            () => provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpUsageProbe.ClientName),
            provider.GetRequiredService<ITextFileReader>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<UsageOptions>(),
            provider.GetRequiredService<ILogger<HttpUsageProbe>>());

    private static IServiceCollection AddActSessions(this IServiceCollection services)
    {
        services.AddSingleton<IAgentEventSink, SessionEventSink>();
        services.AddSingleton<SessionRegistry>();
        services.AddSingleton<SessionLauncher>();
        services.AddSingleton<SessionEventPump>();
        services.AddSingleton<TranscriptPump>();
        services.AddSingleton<SessionRestorer>();

        return services;
    }
}
