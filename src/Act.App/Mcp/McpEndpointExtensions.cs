using Act.Core.Abstractions;
using Act.Core.Agents;
using Microsoft.AspNetCore.Diagnostics;

namespace Act.App.Mcp;

// The web-shaped half of the MCP server, in the app for the reason `HookEndpointExtensions` records:
// this is the only part that needs ASP.NET, and everything decidable without it is behind
// `FollowUpService` and `Act.Core/Spawning` where it can be tested.
//
// It rides the hook listener rather than the app's own port, so the whole agent-facing surface is
// one loopback address the browser never talks to and one per-session secret.
public static class McpEndpointExtensions
{
    public static void AddActMcp(this IServiceCollection services)
    {
        // How a tool learns which task is calling. The SDK's `[McpHeader]` looked like the right
        // expression of that and is not — see `ActMcpTools`.
        services.AddHttpContextAccessor();

        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
                {
                    Name = McpTransport.ServerName,
                    Title = "ACT — Agent Control Tower",
                    Version = typeof(McpEndpointExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                };

                options.ServerInstructions = ActMcpTools.Instructions;
            })
            .WithHttpTransport()
            .WithTools<ActMcpTools>();
    }

    public static void MapActMcp(this IEndpointRouteBuilder routes)
        => routes.MapMcp(McpTransport.Route).DisableAntiforgery();

    // Rejected here rather than only inside the tools, so an unauthorized client never gets as far as
    // a tool list. The tools re-resolve the token anyway — a card can end between the handshake and
    // the call, and only the per-call check sees that.
    public static void UseActMcpAuthorization(this WebApplication app)
    {
        var hooks = app.Services.GetRequiredService<IHookEndpoint>();

        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(McpTransport.Route),
            branch => branch.Use(async (context, next) =>
            {
                // Same reason as the hook endpoint: re-executing a rejection through the Blazor
                // pipeline answers a json post with a content-type 400 instead of the 401 meant.
                if (context.Features.Get<IStatusCodePagesFeature>() is { } pages)
                    pages.Enabled = false;

                var token = context.Request.Headers[HookTransport.TokenHeader].ToString();

                if (!hooks.TryResolve(token, out _))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                    return;
                }

                await next(context);
            }));
    }
}
