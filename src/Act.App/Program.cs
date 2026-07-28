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

builder.Services.AddTransient<ICacheBuster, CacheBuster>();

builder.Services.AddActInfrastructure(
    ActDataDirectory.Resolve(builder.Configuration[ActDataDirectory.OverrideKey]));
builder.Services.AddSingleton<UserSettingsService>();

var launchedByElectron = args.Any(a => a.StartsWith("/electronPort", StringComparison.OrdinalIgnoreCase));
var runElectron = launchedByElectron
    || builder.Configuration.GetValue<bool>("Electron:Enabled");
if (runElectron)
{
    builder.Services.AddElectron();
    builder.UseElectron(args, CreateWindowAsync);
}

var app = builder.Build();

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
    Electron.Menu.SetApplicationMenu(Array.Empty<MenuItem>());

    var window = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions
    {
        Show = false,
        Icon = Path.Combine(AppContext.BaseDirectory, "icon.ico"),
    });

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
