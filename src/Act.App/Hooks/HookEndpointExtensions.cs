using System.Text.Json;
using Act.Core.Model;
using Act.Infrastructure.Hooks;
using Microsoft.AspNetCore.Diagnostics;

namespace Act.App.Hooks;

// The web-shaped half of the hook endpoint: an address on the listener, a middleware, and two
// routes. It lives in the app rather than in `Act.Infrastructure` on purpose — this is the only
// part that needs ASP.NET, and granting a project full of passive port adapters (LiteDB, the pty,
// the filesystem) a web framework reference for the sake of one file is a capability nothing else
// there should have. Everything testable — the token registry, the port allocator, the guard rule,
// the normalizing dispatcher — stays behind in infrastructure with its own tests.
public static class HookEndpointExtensions
{
    private const string DefaultAppAddress = "http://localhost:5000";

    // Two listeners on one host, and Kestrel makes that awkward: configured addresses and explicit
    // `Listen` endpoints are mutually exclusive, and touching either one discards the other. So the
    // app's own address is read back out of configuration and re-added alongside the hook one —
    // `urls` is the key both `applicationUrl` and `UseUrls` end up writing to, which is what makes
    // this work under `dotnet run` and under the Electron shell alike. `docs/design-notes.md`
    // records the two attempts that put the whole UI on the hook port instead.
    //
    // Called after `Build` because the port comes from the store, and before `Run` because an
    // adapter must be able to read the url before any agent can launch.
    public static void BindActHookEndpoint(this WebApplication app)
    {
        var port = app.Services.GetRequiredService<HookEndpointBinder>().Bind();

        foreach (var address in AppAddresses(app))
            app.Urls.Add(address);

        app.Urls.Add($"http://127.0.0.1:{port}");
    }

    // Same web host, a second loopback listener — not a second server. This is what makes the two
    // ports mean different things; without it, adding a listener would simply expose every route
    // twice.
    public static void UseActHookPortGuard(this WebApplication app)
    {
        var endpoint = app.Services.GetRequiredService<HookEndpoint>();

        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "/";

            if (HookPortGuard.Rejects(context.Connection.LocalPort, endpoint.Port, path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;

                return;
            }

            await next(context);
        });
    }

    public static void MapActHooks(this IEndpointRouteBuilder routes)
    {
        foreach (var agent in Enum.GetValues<AgentType>())
            Map(routes, agent);
    }

    private static IEnumerable<string> AppAddresses(WebApplication app)
    {
        if (app.Urls.Count > 0)
            return app.Urls.ToList();

        var configured = app.Configuration[WebHostDefaults.ServerUrlsKey];

        return string.IsNullOrWhiteSpace(configured)
            ? [DefaultAppAddress]
            : configured.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void Map(IEndpointRouteBuilder routes, AgentType agent)
        => routes.MapPost(HookEndpoint.RouteFor(agent), async (HttpContext context, HookRequestHandler handler) =>
            {
                // An agent's hook client gets a status code and nothing else. Without this the app's
                // status-code pages re-execute a rejection through the Blazor pipeline, which then
                // answers a json post with "incorrect Content-type" — a 400 that says nothing true
                // about what happened.
                if (context.Features.Get<IStatusCodePagesFeature>() is { } pages)
                    pages.Enabled = false;

                var token = context.Request.Headers[HookEndpoint.TokenHeader].ToString();
                var payload = await ReadPayloadAsync(context);

                var result = handler.Handle(agent, token, payload);

                context.Response.StatusCode = result is HookResult.Unauthorized
                    ? StatusCodes.Status401Unauthorized
                    : StatusCodes.Status200OK;
            })
            .WithName($"act-hooks-{agent}".ToLowerInvariant())
            .DisableAntiforgery();

    // A body that is not json is not a reason to fail the agent's hook: it becomes an empty
    // payload, normalizes to nothing, and is acked.
    private static async Task<JsonElement> ReadPayloadAsync(HttpContext context)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                context.Request.Body,
                cancellationToken: context.RequestAborted);

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
