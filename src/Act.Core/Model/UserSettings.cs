namespace Act.Core.Model;

public sealed class UserSettings
{
    public LanguagePreference Language { get; set; } = LanguagePreference.System;

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public BoardDensity Density { get; set; } = BoardDensity.Detailed;

    public bool BlinkYourTurn { get; set; } = true;

    public bool KeepAwake { get; set; } = true;

    public bool Notifications { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    // The opt-out for the cloud usage metrics, read on every capture rather than at startup so the
    // switch takes effect the moment it moves — see `ConsentedTelemetrySink`.
    public bool Telemetry { get; set; } = true;

    // The anonymous `distinct_id` those metrics carry: a random GUID, never a machine or hardware
    // identifier, seeded on first launch by `UserSettingsService` and never rewritten. Stored even
    // when telemetry is off, because it is also the id a bug report quotes — the Diagnostics page
    // shows it.
    public string InstallId { get; set; } = string.Empty;

    // On by default: two agents in one working tree is the failure mode that costs work rather than
    // time, and a user who wants them side by side can say so on the card that needs it.
    public bool PreventConcurrentWorkingDir { get; set; } = true;

    // How many cards may be past the launch boundary at once. A real resource ceiling rather than a
    // politeness setting: every one of them holds a live agent process and its pty.
    public int MaxConcurrent { get; set; } = 5;

    // The master switch, off by default — auto-execution is the point of the queue. Manual launches
    // are unaffected: pausing stops ACT from starting things, not the user.
    public bool AutoExecutionPaused { get; set; }

    // Where a quota window counts as spent. Under 100 on purpose — see `UsageCeiling`, which owns the
    // default and the bounds.
    public int UsageCeilingPercent { get; set; } = 98;

    // One entry per agent, holding what its install looks like on this machine. A list rather than
    // a property per agent, so adding an adapter does not mean touching the settings model.
    public IList<AgentDefaults> Agents { get; set; } = [];

    // Whether startup discovery has run at least once. It is what makes "disable an agent ACT
    // cannot find" a first-impression rather than a rule that would keep undoing the user.
    public bool AgentInstallsProbed { get; set; }

    public bool AutoArchiveCompleted { get; set; } = true;

    public int AutoArchiveCompletedAfterDays { get; set; } = 10;

    // The saved starting points a new task can be created from. Exactly one of them carries
    // `IsDefault`: it is what the plain New task button uses and the one entry that cannot be
    // deleted. `UserSettingsService` guarantees it exists, so every reader can assume one.
    public IList<TaskTemplate> Templates { get; set; } = [];

    // Where the desktop window was last left. Null until the shell has run once.
    public WindowBounds? Window { get; set; }
}
