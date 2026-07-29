using Act.App;
using Act.App.Settings;
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

builder.Services.AddTransient<IAssetVersions, AssetVersions>();

builder.Services.AddActInfrastructure(
    ActDataDirectory.Resolve(builder.Configuration[ActDataDirectory.OverrideKey]));
builder.Services.AddSingleton<AppCulture>();
builder.Services.AddSingleton<UserSettingsService>();

#if DEBUG
var seedSampleCards = builder.Configuration.GetValue("Act:SeedSampleCards", true);
if (seedSampleCards)
    builder.Services.AddSingleton<Act.App.Seeding.SampleCardSeeder>();
#endif

var launchedByElectron = args.Any(a => a.StartsWith("/electronPort", StringComparison.OrdinalIgnoreCase));
var runElectron = launchedByElectron
    || builder.Configuration.GetValue<bool>("Electron:Enabled");
if (runElectron)
{
    builder.Services.AddElectron();
    builder.UseElectron(args, CreateWindowAsync);
}

var app = builder.Build();

app.Services.GetRequiredService<UserSettingsService>().ApplyLanguage();

#if DEBUG
if (seedSampleCards)
    await app.Services.GetRequiredService<Act.App.Seeding.SampleCardSeeder>().SeedIfEmptyAsync();
#endif

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

    if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        options.TitleBarOverlay = new TitleBarOverlay
        {
            Color = "#151e26",
            SymbolColor = "#8091a0",
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
