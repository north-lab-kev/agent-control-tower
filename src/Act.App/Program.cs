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

// Before everything: the two ports mean different things, and the guard is what says so — hooks
// answer only on the loopback hook port, the UI only on the app's. It also has to run ahead of
// HTTPS redirection, which would otherwise bounce an agent's plain-http hook post.
app.UseActHookPortGuard();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapActHooks();
app.MapStaticAssets();
app.MapRazorComponents<AppRoot>()
    .AddInteractiveServerRenderMode();

app.Run();

