using Act.Core.Abstractions;
using Act.Infrastructure.Storage;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;

namespace Act.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddActInfrastructure(this IServiceCollection services, string dataDirectory)
    {
        services.AddSingleton<ILiteDatabase>(_ => ActDatabase.Open(dataDirectory));
        services.AddSingleton<ISettingsStore, LiteDbSettingsStore>();

        return services;
    }
}
