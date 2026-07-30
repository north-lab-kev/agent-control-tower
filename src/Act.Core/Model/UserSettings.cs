namespace Act.Core.Model;

public sealed class UserSettings
{
    public LanguagePreference Language { get; set; } = LanguagePreference.System;

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public BoardDensity Density { get; set; } = BoardDensity.Spacious;

    // Off by default: holding someone's machine awake is not something an app may decide for
    // itself, however good its reason.
    public bool KeepAwake { get; set; }
}
