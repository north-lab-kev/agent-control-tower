using Act.Core.Spawning;

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

    // Whether a blank title is filled in for the user by asking the task's own agent — the one place
    // ACT runs a CLI on its own account, which is why it is a switch at all. On by default: nobody
    // should have to name a task twice. Off, a blank title stays blank on the stored card and every
    // screen shows the prompt's opening words in its place — see `CardTitle`.
    public bool GenerateTitles { get; set; } = true;

    // Whether ACT looks for a newer version on its own — and, when it finds one, fetches it. Off
    // means nothing reaches the network unasked; *Check now* still works, and what it finds is
    // offered rather than taken. Desktop-only in effect: a browser tab cannot replace its own
    // installer, so the updater there is a no-op and the setting is hidden.
    //
    // Was a three-way `UpdatePolicy`, collapsed to this by schema 2 — see `docs/design-notes.md`.
    public bool AutoUpdate { get; set; } = true;

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

    // How many follow-ups one card's agent may create over that card's whole life — see `SpawnQuota`.
    // A runaway-loop backstop rather than a considered limit, which is why the default is far above
    // any real plan; zero turns agent-spawned work off entirely.
    public int MaxFollowUpsPerCard { get; set; } = SpawnQuota.Default;

    // Where a quota window counts as spent. Under 100 on purpose — see `UsageCeiling`, which owns the
    // default and the bounds.
    public int UsageCeilingPercent { get; set; } = 95;

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

    // The folder the last created task named. Not a choice the settings page offers: it only
    // pre-fills the new-task form when the template being started from leaves the folder blank.
    public string LastWorkingDir { get; set; } = string.Empty;

    // Where the desktop window was last left. Null until the shell has run once.
    public WindowBounds? Window { get; set; }
}
