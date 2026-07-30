using Act.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Act.Infrastructure.Hooks;

internal static class HookRegistration
{
    // Internal, and called from `AddActInfrastructure`: the endpoint is part of what registering
    // ACT's infrastructure means, and a second public entry point would only invite a composition
    // root that wires half of it. The handler is registered here but cannot be *resolved* until the
    // app supplies the two collaborators that live above this layer — a normalizer per agent and a
    // sink that can find the live session a task id belongs to.
    internal static IServiceCollection AddActHookEndpoint(
        this IServiceCollection services,
        string dataDirectory)
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
