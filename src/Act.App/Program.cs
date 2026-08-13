using Act.App;
using Act.App.Attachments;
using Act.App.Cards;
using Act.App.Desktop;
using Act.App.Sessions;
using Act.App.Settings;
using Act.App.Telemetry;
using Act.App.Updates;
using Act.App.Usage;
using Act.App.Hooks;
using Act.App.Hosting;
using Act.App.Mcp;
using Act.Infrastructure.Logging;
using Act.Infrastructure.Storage;
using ElectronNET.API;
using AppRoot = Act.App.Components.App;

var builder = WebApplication.CreateBuilder(args);

var dataDirectory = ActDataDirectory.Resolve(
    builder.Configuration[ActDataDirectory.OverrideKey],
    builder.Environment.EnvironmentName);

builder.Logging.AddActFileLog(dataDirectory);

// Read here, where nothing has opened the store yet and there is no container to fail inside. Acted on
// after the host is built, which is the first point there is a logger to say it through — and, under
// Electron, reported from the ready callback below, the first point a dialog exists at all.
var store = ActStoreCompatibility.Inspect(dataDirectory);

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
    builder.UseElectron(args, StartDesktopAsync);
}

var app = builder.Build();

host = app;

StartupLog.Report(app, dataDirectory, runElectron);

// **Before the first line that opens the store, because opening it is the thing that fails.** A store
// written by a newer ACT cannot be migrated backwards, so a downgraded build has to refuse it — and the
// refusal has to be said out loud rather than thrown, which is what `ActSchema` would do on the startup
// thread with no window and no bridge to say it through.
//
// Nothing below this may run: every one of those lines resolves a service that opens `act.db`. Under
// Electron the host is still started, and only so far as Electron's ready callback, which is the first
// moment a native dialog exists at all — see `IncompatibleStoreNotice`. In browser mode there is no
// dialog surface, so the log and the exit code are the whole report.
if (!store.IsSupported)
{
    StartupLog.ReportIncompatibleStore(app, store.Stored, store.Understood);

    if (!runElectron)
        return 1;

    app.Run();

    return 1;
}

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

// Everything Electron, in the one callback that means Electron is up. The update pump belongs here
// and not beside the other pumps above, because `ElectronUpdater.Configure` writes
// `Electron.AutoUpdater` properties and every one of those setters goes through the bridge socket —
// which does not exist until this fires. Started earlier, the first write threw a
// `NullReferenceException` on the startup thread and the packaged app died behind its own splash
// screen with nothing on screen to say why. Browser mode never reaches this and needs nothing:
// `BrowserUpdater.IsSupported` is false and the pump stands itself down.
//
// Not folded into `DesktopShell`, which is where it looks like it belongs: `ElectronNotifier` takes
// a `DesktopShell`, so a shell that took the pump would close the loop
// `INotifier -> DesktopShell -> UpdatePump -> INotifier` and the container refuses to build.
async Task StartDesktopAsync()
{
    // The one path that opens no window. Checked here as well as above because this callback is the
    // only place a native dialog can be drawn, and the branch above deliberately runs the host just
    // far enough to reach it.
    if (!store.IsSupported)
    {
        await IncompatibleStoreNotice.ShowAndExitAsync(store);

        return;
    }

    await host!.Services.GetRequiredService<DesktopShell>().StartAsync();

    host!.Services.GetRequiredService<UpdatePump>().Start();
}

// Before everything: the two ports mean different things, and the guard is what says so — hooks
// answer only on the loopback hook port, the UI only on the app's.
app.UseActHookPortGuard();

// After the guard, so a request that reached the wrong port is already a 404 rather than a 401 that
// would confirm the route exists there.
app.UseActMcpAuthorization();

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
app.MapActMcp();
app.MapActAttachments();
app.MapStaticAssets();
app.MapRazorComponents<AppRoot>()
    .AddInteractiveServerRenderMode();

app.Run();

// Reached only on an ordinary shutdown. The refusal above is the one path that exits non-zero, so a
// supervisor can tell "ACT would not open this store" from "ACT closed".
return 0;

