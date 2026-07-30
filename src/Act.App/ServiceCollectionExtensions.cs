using Act.Agents.ClaudeCode;
using Act.Agents.Codex;
using Act.App.Cards;
using Act.App.Desktop;
using Act.App.Sessions;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Infrastructure;
using Act.Infrastructure.Storage;
using ElectronNET.API;
using Radzen;

namespace Act.App;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddActApp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddRadzenComponents();

        services.AddSingleton<IAssetVersions, AssetVersions>();

        services.AddActInfrastructure(
            ActDataDirectory.Resolve(configuration[ActDataDirectory.OverrideKey]));

        return services
            .AddActAgents()
            .AddActBoard()
            .AddActSessions();
    }

    // Electron-only, and registered from the branch that decides the app runs as a desktop shell:
    // resolving DesktopShell without Electron's own services behind it would fail.
    public static IServiceCollection AddActDesktopShell(this IServiceCollection services)
    {
        services.AddElectron();
        services.AddSingleton<DesktopShell>();

        return services;
    }

    private static IServiceCollection AddActAgents(this IServiceCollection services)
    {
        services.AddSingleton<IAgentAdapter, ClaudeCodeAdapter>();
        services.AddSingleton<IAgentAdapter, CodexAdapter>();
        services.AddSingleton<IHookNormalizer, ClaudeCodeHookNormalizer>();
        services.AddSingleton<IHookNormalizer, CodexHookNormalizer>();
        services.AddSingleton<IAgentCapabilityCatalog, AgentCapabilityCatalog>();

        return services;
    }

    private static IServiceCollection AddActBoard(this IServiceCollection services)
    {
        services.AddSingleton<AppCulture>();
        services.AddSingleton<UserSettingsService>();
        services.AddSingleton<BoardState>();
        services.AddSingleton<CardCompleter>();

        return services;
    }

    private static IServiceCollection AddActSessions(this IServiceCollection services)
    {
        services.AddSingleton<IAgentEventSink, SessionEventSink>();
        services.AddSingleton<SessionRegistry>();
        services.AddSingleton<SessionLauncher>();
        services.AddSingleton<SessionEventPump>();
        services.AddSingleton<SessionRestorer>();

        return services;
    }
}
