using Act.App.Notifications;
using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace Act.App.Components.Layout;

public partial class MainLayout(
    UserSettingsService settings,
    IAssetVersions assetVersions,
    NavigationManager navigation,
    UiPresence presence,
    DeepLinkRouter links,
    IJSRuntime js) : IAsyncDisposable
{
    private readonly Guid watcher = Guid.NewGuid();

    private string? markPath;

    private ThemePreference appliedTheme = settings.Theme;

    private DotNetObjectReference<MainLayout>? owner;

    private IJSObjectReference? module;

    private bool focused;

    private string ThemeAttribute => settings.Theme.ToString().ToLowerInvariant();

    private BoardDensity Density => settings.Density;

    private bool BlinkYourTurn => settings.BlinkYourTurn;

    private string MarkPath
        => markPath ??= $"favicon.png{assetVersions.For(typeof(MainLayout).Assembly)}";

    protected override void OnInitialized()
    {
        settings.Changed += OnChanged;
        navigation.LocationChanged += OnLocationChanged;
        links.Requested += OnDeepLink;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        module = await js.InvokeAsync<IJSObjectReference>("import", "/js/act-presence.js");
        owner = DotNetObjectReference.Create(this);

        await module.InvokeVoidAsync("watch", owner);
    }

    [JSInvokable]
    public void OnPresence(bool hasFocus)
    {
        focused = hasFocus;

        Report();
    }

    public async ValueTask DisposeAsync()
    {
        settings.Changed -= OnChanged;
        navigation.LocationChanged -= OnLocationChanged;
        links.Requested -= OnDeepLink;

        presence.Forget(watcher);

        if (module is { } loaded)
        {
            try
            {
                await loaded.InvokeVoidAsync("dispose");
                await loaded.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        owner?.Dispose();
    }

    private void Report()
        => presence.Report(watcher, focused, navigation.ToBaseRelativePath(navigation.Uri));

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args) => Report();

    private void OnDeepLink(Guid taskId)
        => _ = InvokeAsync(() => navigation.NavigateTo($"/card/{taskId}/terminal"));

    private void OnChanged() => _ = InvokeAsync(async () =>
    {
        StateHasChanged();

        if (appliedTheme == settings.Theme)
            return;

        appliedTheme = settings.Theme;

        await js.InvokeVoidAsync(
            "actTheme.apply",
            ThemeMedia.LightId,
            ThemeMedia.Light(appliedTheme),
            ThemeMedia.DarkId,
            ThemeMedia.Dark(appliedTheme));
    });

    private void OpenNewTask() => navigation.NavigateTo("/card/new");

    private void OpenArchive() => navigation.NavigateTo("/archive");

    private void OpenSettings() => navigation.NavigateTo("/settings");
}
