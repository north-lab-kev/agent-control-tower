using Act.App;
using Act.App.Cards;
using Act.App.Desktop;
using Act.App.Sessions;
using Act.App.Settings;
using Act.App.Usage;
using Act.App.Hooks;
using ElectronNET.API;
using AppRoot = Act.App.Components.App;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddActApp(builder.Configuration);

var launchedByElectron = args.Any(a => a.StartsWith("/electronPort", StringComparison.OrdinalIgnoreCase));
var runElectron = launchedByElectron
    || builder.Configuration.GetValue<bool>("Electron:Enabled");
// Assigned right after Build and read only when Electron signals ready, which is later still —
// the callback has to reach a container that does not exist yet at the point it is registered.
WebApplication? host = null;

if (runElectron)
{
    builder.Services.AddActDesktopShell();
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
app.Services.GetRequiredService<TranscriptPump>().Start();
app.Services.GetRequiredService<UsagePump>().Start();

// The terminals that died with the previous run come back here, and only once the host is actually
// listening: a resumed agent posts its first hook within moments of starting, and the endpoint that
// answers it is this app's. Not awaited — restoring several agents is several process spawns, and
// none of them is a reason to hold up the window.
app.Lifetime.ApplicationStarted.Register(
    () => _ = app.Services.GetRequiredService<SessionRestorer>().RestoreAllAsync(app.Lifetime.ApplicationStopping));

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

