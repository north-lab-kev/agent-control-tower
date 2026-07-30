using Act.Agents.ClaudeCode;
using Act.Agents.Codex;
using Act.App;
using Act.App.Cards;
using Act.App.Desktop;
using Act.App.Sessions;
using Act.App.Settings;
using Act.App.Hooks;
using Act.Core.Abstractions;
using Act.Infrastructure;
using Act.Infrastructure.Storage;
using ElectronNET.API;
using ElectronNET.API.Entities;
using Radzen;
using AppRoot = Act.App.Components.App;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRadzenComponents();

builder.Services.AddSingleton<IAssetVersions, AssetVersions>();

builder.Services.AddActInfrastructure(
    ActDataDirectory.Resolve(builder.Configuration[ActDataDirectory.OverrideKey]));
builder.Services.AddSingleton<IAgentAdapter, ClaudeCodeAdapter>();
builder.Services.AddSingleton<IAgentAdapter, CodexAdapter>();
builder.Services.AddSingleton<IHookNormalizer, ClaudeCodeHookNormalizer>();
builder.Services.AddSingleton<IHookNormalizer, CodexHookNormalizer>();
builder.Services.AddSingleton<IAgentEventSink, SessionEventSink>();
builder.Services.AddSingleton<IAgentCapabilityCatalog, AgentCapabilityCatalog>();
builder.Services.AddSingleton<AppCulture>();
builder.Services.AddSingleton<UserSettingsService>();
builder.Services.AddSingleton<BoardState>();
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddSingleton<SessionLauncher>();
builder.Services.AddSingleton<SessionEventPump>();

var launchedByElectron = args.Any(a => a.StartsWith("/electronPort", StringComparison.OrdinalIgnoreCase));
var runElectron = launchedByElectron
    || builder.Configuration.GetValue<bool>("Electron:Enabled");
// Assigned right after Build and read only when Electron signals ready, which is later still —
// the callback has to reach a container that does not exist yet at the point it is registered.
WebApplication? host = null;

if (runElectron)
{
    builder.Services.AddElectron();
    builder.Services.AddSingleton<DesktopShell>();
    builder.UseElectron(args, () => host!.Services.GetRequiredService<DesktopShell>().StartAsync());
}

var app = builder.Build();

host = app;

app.BindActHookEndpoint();

var settings = app.Services.GetRequiredService<UserSettingsService>();

settings.ApplyLanguage();
settings.ApplyKeepAwake();

await app.Services.GetRequiredService<BoardState>().LoadAsync();

// Started before anything can launch an agent, and explicitly rather than on first resolve: a pump
// that attaches late has already missed the events it exists to read.
app.Services.GetRequiredService<SessionEventPump>().Start();

// Before everything: the two ports mean different things, and the guard is what says so — hooks
// answer only on the loopback hook port, the UI only on the app's.
app.UseActHookPortGuard();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Deliberately no HTTPS redirection and no HSTS. ACT is a desktop app whose web host is an
// implementation detail: the UI is a local window under Electron, there is no certificate to serve
// and nothing reaches it from off the machine. Both were template defaults, and both were worse
// than inert here — redirection logged "failed to determine the https port" on every start under
// the http profile, and under the https one it would have found a port and bounced every
// plain-http request to it, **including the agents' hook posts on the loopback port**. A hook
// client does not follow a 307, so ingestion would have died quietly on that profile. HSTS only
// ever applied to the packaged build, where a policy pinned to localhost is a liability to every
// other local app on the machine rather than a protection for this one.
app.UseAntiforgery();

app.MapActHooks();
app.MapStaticAssets();
app.MapRazorComponents<AppRoot>()
    .AddInteractiveServerRenderMode();

app.Run();

