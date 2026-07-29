using Act.App.Settings;

namespace Act.App.Components.Layout;

// Rendered statically on purpose: an interactive component's prerendered DOM is discarded
// and re-rendered when the circuit starts, and re-inserting a <link> makes the browser
// repaint unstyled. Runtime theme changes flip the `media` attributes through
// ThemeStylesheets.razor.js instead of re-rendering these elements.
public partial class ThemeStylesheets(UserSettingsService settings, IAssetVersions assetVersions)
{
    private string LightHref => Href("standard");

    private string DarkHref => Href("standard-dark");

    private string LightMedia => ThemeMedia.Light(settings.Theme);

    private string DarkMedia => ThemeMedia.Dark(settings.Theme);

    private string Href(string theme)
        => Assets[$"_content/Radzen.Blazor/css/{theme}.css"] + assetVersions.For(typeof(Radzen.Colors).Assembly);
}
