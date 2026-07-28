using Act.App.Settings;
using Act.Core.Model;
using Radzen;

namespace Act.App.Components.Settings;

public partial class SettingsDialog(UserSettingsService settings, DialogService dialogService)
{
    private static readonly SettingChoice<ThemePreference>[] ThemeChoices =
    [
        new(ThemePreference.System, "Based on the system"),
        new(ThemePreference.Dark, "Dark"),
        new(ThemePreference.Light, "Light"),
    ];

    private static readonly SettingChoice<BoardDensity>[] DensityChoices =
    [
        new(BoardDensity.Spacious, "Spacious"),
        new(BoardDensity.Compact, "Compact"),
    ];

    private ThemePreference Theme => settings.Theme;

    private BoardDensity Density => settings.Density;

    private void OnThemeChanged(ThemePreference theme) => settings.SetTheme(theme);

    private void OnDensityChanged(BoardDensity density) => settings.SetDensity(density);

    private void Close() => dialogService.Close();
}
