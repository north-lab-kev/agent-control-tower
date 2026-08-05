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
        services.AddActHookEndpoint(dataDirectory);

        return services;
    }
}
