using Act.Agents.ClaudeCode;
using Act.Agents.Codex;
using Act.App;
using Act.App.Cards;
using Act.App.Sessions;
using Act.App.Settings;
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
builder.Services.AddSingleton<IAgentCapabilityCatalog, AgentCapabilityCatalog>();
builder.Services.AddSingleton<AppCulture>();
builder.Services.AddSingleton<UserSettingsService>();
builder.Services.AddSingleton<BoardState>();
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddSingleton<SessionLauncher>();

var launchedByElectron = args.Any(a => a.StartsWith("/electronPort", StringComparison.OrdinalIgnoreCase));
var runElectron = launchedByElectron
    || builder.Configuration.GetValue<bool>("Electron:Enabled");
if (runElectron)
{
    builder.Services.AddElectron();
    builder.UseElectron(args, CreateWindowAsync);
}

var app = builder.Build();

var settings = app.Services.GetRequiredService<UserSettingsService>();

settings.ApplyLanguage();
settings.ApplyKeepAwake();

await app.Services.GetRequiredService<BoardState>().LoadAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<AppRoot>()
    .AddInteractiveServerRenderMode();

app.Run();

static async Task CreateWindowAsync()
{
    const int titleBarHeight = 49;

    Electron.Menu.SetApplicationMenu(Array.Empty<MenuItem>());

    var options = new BrowserWindowOptions
    {
        Show = false,
        Icon = Path.Combine(AppContext.BaseDirectory, "icon.ico"),
        TitleBarStyle = TitleBarStyle.hidden,
    };

    // Transparent on purpose. The overlay is native and can only be coloured at window creation,
    // so any fixed colour is wrong half the time — it cannot follow the theme, and it painted a
    // flat block over the end of a bar that is a gradient with a rule under it. Left transparent,
    // the page paints the whole strip and the seam disappears; only the glyphs stay native, in a
    // grey chosen to read on both themes.
    if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        options.TitleBarOverlay = new TitleBarOverlay
        {
            Color = "rgba(0, 0, 0, 0)",
            SymbolColor = "#7c8b99",
            Height = titleBarHeight,
        };

    var window = await Electron.WindowManager.CreateWindowAsync(options);

    window.OnReadyToShow += () => window.Show();

    await Electron.App.RequestSingleInstanceLockAsync(
        (_, _) => RevealWindow(window),
        CancellationToken.None);
}

static void RevealWindow(BrowserWindow window)
{
    window.Restore();
    window.Show();
    window.Focus();
}
