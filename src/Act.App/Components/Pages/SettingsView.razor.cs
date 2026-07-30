using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Model;
using ElectronNET.API;
using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Pages;

// Every setting applies and persists the moment it changes, so there is no Save and no Cancel —
// the only action is leaving, which is what the back arrow is for.
public partial class SettingsView(UserSettingsService settings, NavigationManager navigation)
{
    private static SettingChoice<LanguagePreference>[] LanguageChoices =>
    [
        new(LanguagePreference.System, Strings.Settings_Language_System),
        new(LanguagePreference.English, "English"),
        new(LanguagePreference.French, "Français"),
    ];

    private static SettingChoice<ThemePreference>[] ThemeChoices =>
    [
        new(ThemePreference.System, Strings.Settings_Theme_System),
        new(ThemePreference.Dark, Strings.Settings_Theme_Dark),
        new(ThemePreference.Light, Strings.Settings_Theme_Light),
    ];

    private static SettingChoice<BoardDensity>[] DensityChoices =>
    [
        new(BoardDensity.Spacious, Strings.Settings_Density_Spacious),
        new(BoardDensity.Compact, Strings.Settings_Density_Compact),
    ];

    private LanguagePreference Language => settings.Language;

    private ThemePreference Theme => settings.Theme;

    private BoardDensity Density => settings.Density;

    private bool KeepAwake => settings.KeepAwake;

    private bool CloseToTray => settings.CloseToTray;

    private static bool IsDesktop => HybridSupport.IsElectronActive;

    // A language change has to re-run the whole render tree under the new culture, so it reloads.
    // As a page that now lands the user back on settings rather than on the board, which is where
    // they were — the reload is no longer also a dismissal.
    private void OnLanguageChanged(LanguagePreference language)
    {
        if (language == settings.Language)
            return;

        settings.SetLanguage(language);
        navigation.Refresh(forceReload: true);
    }

    private void OnThemeChanged(ThemePreference theme) => settings.SetTheme(theme);

    private void OnDensityChanged(BoardDensity density) => settings.SetDensity(density);

    private void OnKeepAwakeChanged(bool keepAwake) => settings.SetKeepAwake(keepAwake);

    private void OnCloseToTrayChanged(bool closeToTray) => settings.SetCloseToTray(closeToTray);

    private void BackToBoard() => navigation.NavigateTo("/");
}
