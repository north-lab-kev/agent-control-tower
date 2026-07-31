namespace Act.Core.Model;

public sealed class UserSettings
{
    public LanguagePreference Language { get; set; } = LanguagePreference.System;

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public BoardDensity Density { get; set; } = BoardDensity.Spacious;

    public bool BlinkYourTurn { get; set; } = true;

    public bool KeepAwake { get; set; } = true;

    public bool CloseToTray { get; set; } = true;
}
