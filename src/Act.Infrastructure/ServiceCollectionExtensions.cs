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
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IWorkingDirectories, WorkingDirectories>();
        services.AddSingleton<IPtyHost, PtyHost>();
        services.AddSingleton<ICommandHost, CommandHost>();
        services.AddSingleton<IExecutableProbe, ExecutableProbe>();
        services.AddSingleton<ISleepInhibitor, SleepInhibitor>();
        services.AddSingleton<ILiteDatabase>(provider => ActDatabase.Open(
            dataDirectory,
            provider.GetService<ILoggerFactory>()?.CreateLogger(typeof(ActDatabase))));
        services.AddSingleton<ISettingsStore, LiteDbSettingsStore>();
        services.AddSingleton<ICardStore, LiteDbCardStore>();
        services.AddSingleton<ITranscriptReader, TranscriptReader>();
        services.AddSingleton<ITextFileReader, TextFileReader>();
        services.AddActHookEndpoint(dataDirectory);

        return services;
    }
}
