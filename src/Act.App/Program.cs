using Act.App;
using Act.App.Attachments;
using Act.App.Cards;
using Act.App.Desktop;
using Act.App.Sessions;
using Act.App.Settings;
using Act.App.Telemetry;
using Act.App.Usage;
using Act.App.Hooks;
using Act.App.Hosting;
using Act.Infrastructure.Logging;
using Act.Infrastructure.Storage;
using ElectronNET.API;
using AppRoot = Act.App.Components.App;

var builder = WebApplication.CreateBuilder(args);

var dataDirectory = ActDataDirectory.Resolve(
    builder.Configuration[ActDataDirectory.OverrideKey],
    builder.Environment.EnvironmentName);

builder.Logging.AddActFileLog(dataDirectory);

builder.Services.AddActApp(builder.Configuration, dataDirectory);

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

StartupLog.Report(app, dataDirectory, runElectron);

app.BindActHookEndpoint();

var settings = app.Services.GetRequiredService<UserSettingsService>();

settings.ApplyLanguage();

// Straight after the language, because the locale it just set is one of the things `app_started`
// reports, and before everything else, because the crash handlers it attaches are worth having up
// before anything can fail. The container disposes it, which is where the closing flush lives.
app.Services.GetRequiredService<TelemetryPump>().Start();

await app.Services.GetRequiredService<BoardState>().LoadAsync();

// Straight after the load and before anything can create a card, which is the only moment "no card
// owns this folder" is a fact rather than a race — see `AttachmentSweep`.
app.Services.GetRequiredService<AttachmentSweep>().Run();

// Before anything can launch: discovery only fills what settings do not already say, because a
// path the user typed is the answer — probing is for the machine nobody has configured yet.
app.Services.GetRequiredService<AgentInstallDiscovery>().Run();

// Started before anything can launch an agent, and explicitly rather than on first resolve: a pump
// that attaches late has already missed the events it exists to read.
app.Services.GetRequiredService<SessionEventPump>().Start();
app.Services.GetRequiredService<TranscriptPump>().Start();
app.Services.GetRequiredService<UsagePump>().Start();
app.Services.GetRequiredService<RetentionPump>().Start();

// The terminals that died with the previous run come back here, and only once the host is actually
// listening: a resumed agent posts its first hook within moments of starting, and the endpoint that
// answers it is this app's. Not awaited — restoring several agents is several process spawns, and
// none of them is a reason to hold up the window.
//
// The queue runner starts *after* it, and that ordering is load-bearing: a restored card occupies a
// concurrency slot and holds its working directory, so a runner that went first would judge an
// empty board and launch straight past the cap.
app.Lifetime.ApplicationStarted.Register(() => _ = RestoreThenRunAsync());

async Task RestoreThenRunAsync()
{
    await app.Services.GetRequiredService<SessionRestorer>().RestoreAllAsync(app.Lifetime.ApplicationStopping);

    app.Services.GetRequiredService<QueueRunner>().Start();
}

// Before everything: the two ports mean different things, and the guard is what says so — hooks
// answer only on the loopback hook port, the UI only on the app's.
app.UseActHookPortGuard();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Deliberately no HTTPS redirection and no HSTS: ACT is a desktop app whose web host is an
// implementation detail, and redirection would bounce the agents' hook posts off the loopback port
// to a 307 no hook client follows. See *No HTTPS redirection and no HSTS* in `docs/design-notes.md`
// before adding either back.
app.UseAntiforgery();

app.MapActHooks();
app.MapActAttachments();
app.MapStaticAssets();
app.MapRazorComponents<AppRoot>()
    .AddInteractiveServerRenderMode();

app.Run();

