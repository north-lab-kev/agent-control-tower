namespace Act.Core.Model;

public sealed class UserSettings
{
    public LanguagePreference Language { get; set; } = LanguagePreference.System;

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public BoardDensity Density { get; set; } = BoardDensity.Spacious;

    public bool BlinkYourTurn { get; set; } = true;

    public bool KeepAwake { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    // One entry per agent, holding what its install looks like on this machine. A list rather than
    // a property per agent, so adding an adapter does not mean touching the settings model.
    public IList<AgentDefaults> Agents { get; set; } = [];

    // Whether the one-time lift of the old per-card executable / flags / environment has happened.
    // An explicit marker rather than "does an entry exist", which is what the first version inferred
    // from and got wrong: a settings row written by any earlier build — or by the user touching a
    // field once — made the lift look done, and the value it was supposed to rescue stayed lost with
    // no way back short of retyping it.
    public bool AgentDefaultsLifted { get; set; }

    // Whether startup discovery has run at least once. It is what makes "disable an agent ACT
    // cannot find" a first-impression rather than a rule that would keep undoing the user.
    public bool AgentInstallsProbed { get; set; }

    public bool AutoArchiveCompleted { get; set; } = true;

    public int AutoArchiveCompletedAfterDays { get; set; } = 10;

    // What a new task is pre-filled with. Never null, so the form can read it unconditionally.
    public TaskDefaults TaskDefaults { get; set; } = new();
}
