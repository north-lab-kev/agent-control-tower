namespace Act.Core.Model;

public sealed class UserSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public BoardDensity Density { get; set; } = BoardDensity.Spacious;
}
