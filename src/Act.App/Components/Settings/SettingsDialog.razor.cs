using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Model;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace Act.App.Components.Settings;

public partial class SettingsDialog(
    UserSettingsService settings,
    DialogService dialogService,
    NavigationManager navigation)
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

    private void OnLanguageChanged(LanguagePreference language)
    {
        if (language == settings.Language)
            return;

        settings.SetLanguage(language);
        dialogService.Close();
        navigation.Refresh(forceReload: true);
    }

    private void OnThemeChanged(ThemePreference theme) => settings.SetTheme(theme);

    private void OnDensityChanged(BoardDensity density) => settings.SetDensity(density);

    private void Close() => dialogService.Close();
}
