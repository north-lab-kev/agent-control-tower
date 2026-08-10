using Act.Core.Abstractions;
using Act.Infrastructure.FileSystem;
using Act.Infrastructure.Hooks;
using Act.Infrastructure.Power;
using Act.Infrastructure.Storage;
using Act.Infrastructure.Terminal;
using Act.Infrastructure.Transcripts;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Act.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddActInfrastructure(this IServiceCollection services, string dataDirectory)
    {
        // Idempotent, and it is what lets everything here take a plain `ILogger` rather than a
        // nullable one: a caller that never configured logging still gets a factory, so the store
        // does not have to carry a no-logger branch it can never exercise in the app.
        services.AddLogging();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IWorkingDirectories, WorkingDirectories>();
        services.AddSingleton<IAttachmentStore>(provider => new AttachmentStore(
            dataDirectory,
            provider.GetRequiredService<IClock>()));
        services.AddSingleton<IPtyHost, PtyHost>();
        services.AddSingleton<ICommandHost, CommandHost>();
        services.AddSingleton<IExecutableProbe, ExecutableProbe>();
        services.AddSingleton<ISleepInhibitor, SleepInhibitor>();
        services.AddSingleton<ILiteDatabase>(provider => ActDatabase.Open(
            dataDirectory,
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ActDatabase))));
        services.AddSingleton<ISettingsStore, LiteDbSettingsStore>();
        services.AddSingleton<ICardStore, LiteDbCardStore>();
        services.AddSingleton<ITranscriptReader, TranscriptReader>();
        services.AddSingleton<ITextFileReader, TextFileReader>();
        services.AddSingleton<IFileWatcher, FileWatcher>();

        return services.AddHookEndpoint(dataDirectory);
    }

    // Private and grouped rather than a second public entry point: the endpoint is part of what
    // registering ACT's infrastructure *means*, and a separate one would only invite a composition
    // root that wires half of it.
    //
    // The handler is registered here but cannot be *resolved* until the app supplies the two
    // collaborators that live above this layer — a normalizer per agent, and a sink that can find the
    // live session a task id belongs to.
    private static IServiceCollection AddHookEndpoint(this IServiceCollection services, string dataDirectory)
    {
        services.AddSingleton<IAgentConfigFiles>(_ => new AgentConfigFiles(dataDirectory));
        services.AddSingleton<HookPortStore>();
        services.AddSingleton<HookEndpoint>();
        services.AddSingleton<IHookEndpoint>(provider => provider.GetRequiredService<HookEndpoint>());
        services.AddSingleton(provider => new HookEndpointBinder(
            provider.GetRequiredService<HookEndpoint>(),
            provider.GetRequiredService<HookPortStore>()));
        services.AddSingleton<HookRequestHandler>();

        return services;
    }
}
