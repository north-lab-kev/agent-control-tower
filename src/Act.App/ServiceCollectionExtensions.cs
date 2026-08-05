using Act.Agents.ClaudeCode;
using Act.Agents.Codex;
using Act.App.Attachments;
using Act.App.Cards;
using Act.App.Desktop;
using Act.App.Notifications;
using Act.App.Sessions;
using Act.App.Settings;
using Act.App.Telemetry;
using Act.App.Usage;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Infrastructure;
using Act.Infrastructure.Telemetry;
using Act.Infrastructure.Usage;
using ElectronNET.API;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Radzen;

namespace Act.App;

public static class ServiceCollectionExtensions
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddActApp(
        this IServiceCollection services,
        IConfiguration configuration,
        string dataDirectory)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddRadzenComponents();
        services.AddSingleton<IAssetVersions, AssetVersions>();
        services.AddScoped<IDesktopBridge, BrowserDesktopBridge>();

        // Scoped because the bridge is: which shell is behind it is decided per circuit.
        services.AddScoped<AttachmentOpener>();
        services.AddSingleton<INotifier, BrowserNotifier>();
        services.AddSingleton<UiPresence>();
        services.AddSingleton<DeepLinkRouter>();
        services.AddSingleton<NotificationDispatcher>();
        services.AddActInfrastructure(dataDirectory);

        return services
            .AddActAgents()
            .AddActBoard()
            .AddActSessions()
            .AddActUsage(configuration)
            .AddTelemetry(configuration);
    }

    // Electron-only, and registered from the branch that decides the app runs as a desktop shell:
    // resolving DesktopShell without Electron's own services behind it would fail.
    public static IServiceCollection AddActDesktopShell(this IServiceCollection services)
    {
        services.AddElectron();
        services.AddSingleton<DesktopShell>();
        services.Replace(ServiceDescriptor.Scoped<IDesktopBridge, ElectronDesktopBridge>());
        services.Replace(ServiceDescriptor.Singleton<INotifier, ElectronNotifier>());

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
        services.AddSingleton<AgentInstallDiscovery>();
        services.AddSingleton<BoardState>();
        services.AddSingleton<TaskTitles>();
        services.AddSingleton<RetentionPump>();
        services.AddSingleton<AttachmentSweep>();
        services.AddSingleton<CardCompleter>();
        services.AddSingleton<CardReopener>();

        return services;
    }

    private static IServiceCollection AddActUsage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(
            configuration.GetSection(UsageOptions.SectionName).Get<UsageOptions>() ?? new UsageOptions());

        services.AddHttpClient(HttpUsageProbe.ClientName, client => client.Timeout = RequestTimeout);

        services.AddSingleton<IUsageProbe>(provider => Probe(provider, new ClaudeCodeUsageDialect()));
        services.AddSingleton<IUsageProbe>(provider => Probe(provider, new CodexUsageDialect()));

        // Claude Code only: its credential file states an expiry, so `Expired` is a fact ACT knows
        // locally and can do something about. Codex buries expiry in the JWT and reaches the same
        // state as `Unauthorized`, which no local nudge can tell apart from a revoked login.
        services.AddSingleton<IUsageRefresher>(provider => new ClaudeCodeUsageRefresher(
            () => provider.GetServices<IAgentAdapter>().Single(a => a.Agent == AgentType.ClaudeCode),
            () => provider.GetRequiredService<UserSettingsService>().Defaults(AgentType.ClaudeCode)));

        services.AddSingleton<UsageState>();
        services.AddSingleton<UsagePump>();

        return services;
    }

    // Bound here rather than inside `AddActTelemetryClient` for the same reason `AddActUsage` binds
    // its own section: configuration is the app's, and infrastructure takes the answer.
    //
    // The sink is composed in two layers on purpose — the transport knows how to send, the gate knows
    // whether it may. Nothing here names the vendor; `ActTelemetry` never reads the user's switch.
    private static IServiceCollection AddTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>()
            ?? new TelemetryOptions();

        services.AddSingleton(options);

        // Resolved once here, not on each check: the predicate is called from the client's send path,
        // and the run's last send happens while the container is going away. `UserSettingsService`
        // holds nothing disposable, so the closure outliving the provider is safe — asking the
        // provider at that point is not.
        services.AddActTelemetryClient(options, provider =>
        {
            var settings = provider.GetRequiredService<UserSettingsService>();

            return () => settings.Telemetry;
        });

        services.AddSingleton<ITelemetrySink>(provider => new ConsentedTelemetrySink(
            ActTelemetry.Transport(
                provider,
                options,
                provider.GetRequiredService<UserSettingsService>().InstallId),
            provider.GetRequiredService<UserSettingsService>()));

        services.AddSingleton<CrashReports>();
        services.AddSingleton<TelemetryPump>();

        // A method group, so nothing is resolved until a record actually arrives: resolving the sink
        // eagerly here would have it ask for its own `ILogger` while the logger factory is still
        // assembling the providers — one of which is this.
        services.AddSingleton<ILoggerProvider>(provider =>
            new TelemetryErrorBridge(provider.GetRequiredService<CrashReports>));

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
        services.AddSingleton<TerminalGeometry>();
        services.AddSingleton<QueueRunner>();
        services.AddSingleton<SessionEventPump>();
        services.AddSingleton<TranscriptPump>();
        services.AddSingleton<SessionRestorer>();

        return services;
    }
}
