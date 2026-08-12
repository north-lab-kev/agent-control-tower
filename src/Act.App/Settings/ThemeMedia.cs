using Act.Core.Model;

namespace Act.App.Settings;

// Which of the two Radzen stylesheets applies, expressed as a `media` attribute so the
// choice costs no JavaScript on first paint. `follow the OS` defers to the media query;
// an explicit override enables one sheet and disables the other outright.
public static class ThemeMedia
{
    public const string LightId = "act-theme-light";

    public const string DarkId = "act-theme-dark";

    private const string Always = "all";

    private const string Never = "not all";

    public static string Light(ThemePreference theme) => theme switch
    {
        ThemePreference.Light => Always,
        ThemePreference.Dark => Never,
        _ => "(prefers-color-scheme: light)",
    };

    public static string Dark(ThemePreference theme) => theme switch
    {
        ThemePreference.Dark => Always,
        ThemePreference.Light => Never,
        _ => "(prefers-color-scheme: dark)",
    };
}
