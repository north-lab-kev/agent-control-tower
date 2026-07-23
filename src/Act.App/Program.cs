using ElectronNET.API;
using ElectronNET.API.Entities;
using AppRoot = Act.App.Components.App;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var runElectron = HybridSupport.IsElectronActive
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
    });

    window.OnReadyToShow += () => window.Show();
}
