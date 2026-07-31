using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Act.App.Components.Layout;

public partial class MainLayout(
    UserSettingsService settings,
    IAssetVersions assetVersions,
    NavigationManager navigation,
    IJSRuntime js) : IDisposable
{
    private string? markPath;

    private ThemePreference appliedTheme = settings.Theme;

    private string ThemeAttribute => settings.Theme.ToString().ToLowerInvariant();

    private BoardDensity Density => settings.Density;

    private bool BlinkYourTurn => settings.BlinkYourTurn;

    private string MarkPath
        => markPath ??= $"favicon.png{assetVersions.For(typeof(MainLayout).Assembly)}";

    protected override void OnInitialized() => settings.Changed += OnChanged;

    public void Dispose() => settings.Changed -= OnChanged;

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
