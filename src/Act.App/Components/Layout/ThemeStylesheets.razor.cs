using Act.App.Settings;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Layout;

public partial class ThemeStylesheets(UserSettingsService settings, IAssetVersions assetVersions) : IDisposable
{
    private const string Enabled = "all";

    private const string Disabled = "not all";

    private string LightHref => Href("standard");

    private string DarkHref => Href("standard-dark");

    private string LightMedia => settings.Theme switch
    {
        ThemePreference.Light => Enabled,
        ThemePreference.Dark => Disabled,
        _ => "(prefers-color-scheme: light)",
    };

    private string DarkMedia => settings.Theme switch
    {
        ThemePreference.Dark => Enabled,
        ThemePreference.Light => Disabled,
        _ => "(prefers-color-scheme: dark)",
    };

    protected override void OnInitialized() => settings.Changed += OnSettingsChanged;

    public void Dispose() => settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged() => _ = InvokeAsync(StateHasChanged);

    private string Href(string theme)
        => Assets[$"_content/Radzen.Blazor/css/{theme}.css"] + assetVersions.For(typeof(Radzen.Colors).Assembly);
}
