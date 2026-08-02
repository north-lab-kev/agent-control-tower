using Act.App.Desktop;
using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Core.Scheduling;
using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Pages;

// Every setting applies and persists the moment it changes, so there is no Save and no Cancel —
// the only action is leaving, which is what the back arrow is for.
public partial class SettingsView(
    UserSettingsService settings,
    IEnumerable<IAgentAdapter> adapters,
    IAgentCapabilityCatalog capabilities,
    IExecutableProbe probe,
    IDesktopBridge desktop,
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

    private bool BlinkYourTurn => settings.BlinkYourTurn;

    private bool KeepAwake => settings.KeepAwake;

    private bool Notifications => settings.Notifications;

    private bool CloseToTray => settings.CloseToTray;

    private bool PreventConcurrentWorkingDir => settings.PreventConcurrentWorkingDir;

    private bool AutoExecutionPaused => settings.AutoExecutionPaused;

    private int MaxConcurrent => settings.MaxConcurrent;

    private static int MinimumConcurrent => ConcurrencySlots.Minimum;

    private static int MaximumConcurrent => ConcurrencySlots.Maximum;

    private bool IsDesktop => desktop.IsDesktop;

    private bool AutoArchiveCompleted => settings.AutoArchiveCompleted;

    private int AutoArchiveCompletedAfterDays => settings.AutoArchiveCompletedAfterDays;

    private static int MinimumRetentionDays => CompletedRetention.MinimumDays;

    private static int MaximumRetentionDays => CompletedRetention.MaximumDays;

    // Every registered adapter gets a section, in the order they were registered — the settings
    // model holds a list rather than a property per agent for the same reason.
    private List<AgentDefaultsForm> AgentForms { get; } = [];

    private static string AgentName(AgentType agent) => agent switch
    {
        AgentType.Codex => Strings.Agent_Codex,
        _ => Strings.Agent_ClaudeCode,
    };

    private TaskDefaults defaults = new();

    private IReadOnlyList<SettingChoice<AgentType>> DefaultAgentChoices
        => [.. adapters.Select(adapter => adapter.Agent)
            .Select(agent => new SettingChoice<AgentType>(agent, AgentName(agent)))];

    private IReadOnlyList<string> DefaultModels
        => [.. capabilities.For(defaults.Agent).Models.Select(model => model.Slug)];

    private IReadOnlyList<string> DefaultEfforts => capabilities.For(defaults.Agent).EffortsFor(defaults.Model);

    private IReadOnlyList<SettingChoice<PermissionMode>> DefaultPermissionChoices
        => [.. capabilities.For(defaults.Agent).PermissionModes
            .Select(mode => new SettingChoice<PermissionMode>(mode, PermissionLabel(mode)))];

    private static SettingChoice<TaskSchedule>[] DefaultScheduleChoices =>
    [
        new(TaskSchedule.Manual, Strings.ScheduleOption_Manual),
        new(TaskSchedule.Now, Strings.ScheduleOption_Now),
        new(TaskSchedule.NextWindow, Strings.ScheduleOption_NextWindow),
        new(TaskSchedule.WindowAfterNext, Strings.ScheduleOption_WindowAfterNext),
    ];

    private static SettingChoice<GitAction?>[] DefaultGitChoices =>
    [
        new(GitAction.Commit, Strings.GitAction_Commit),
        new(GitAction.Push, Strings.GitAction_Push),
        new(GitAction.PullRequest, Strings.GitAction_PullRequest),
    ];

    private static string PermissionLabel(PermissionMode mode) => mode switch
    {
        PermissionMode.Default => Strings.Permission_Default,
        PermissionMode.Plan => Strings.Permission_Plan,
        PermissionMode.AcceptEdits => Strings.Permission_AcceptEdits,
        PermissionMode.Auto => Strings.Permission_Auto,
        PermissionMode.DontAsk => Strings.Permission_DontAsk,
        PermissionMode.Bypass => Strings.Permission_Bypass,
        _ => mode.ToString(),
    };

    protected override void OnInitialized()
    {
        foreach (var adapter in adapters)
        {
            var form = AgentDefaultsForm.From(settings.Defaults(adapter.Agent));

            form.State = AgentBinaryCheck.For(probe, adapter, form.Binary);

            AgentForms.Add(form);
        }

        defaults = settings.TaskDefaults;
    }

    private static string? BinaryWarning(AgentDefaultsForm agent) => agent.State switch
    {
        { Status: AgentBinaryStatus.NotFound } => Strings.Settings_AgentBinary_NotFound,
        { Status: AgentBinaryStatus.NotInstalled } => Strings.Settings_AgentBinary_NotInstalled,
        { Status: AgentBinaryStatus.NotOnPath, DiscoveredPath: { } path }
            => Text.Format(Strings.Settings_AgentBinary_NotOnPath, path),
        _ => null,
    };

    private static string? BinaryNote(AgentDefaultsForm agent) => agent.State.Status switch
    {
        AgentBinaryStatus.OnPath => Strings.Settings_AgentBinary_OnPath,
        AgentBinaryStatus.Found => Strings.Settings_AgentBinary_Found,
        _ => null,
    };

    // Offered only where there is a specific path to adopt — the fix for "installed but not on
    // PATH" is one click, and making the user retype what ACT just found would be perverse.
    private void UseDiscovered(AgentDefaultsForm agent)
    {
        if (agent.State.DiscoveredPath is { } path)
            OnBinaryChanged(agent, path);
    }

    private void OnEnabledChanged(AgentDefaultsForm agent, bool enabled)
    {
        agent.Enabled = enabled;

        settings.SetDefaults(agent.ToDefaults());
    }

    private bool browsingDefaultDir;

    private void OnDefaultWorkingDirChanged(string value) => SaveDefaults(() => defaults.WorkingDir = value);

    // The picked path is absolute and real, so it replaces whatever was typed and the picker
    // closes — the same behaviour as the task form's.
    private void OnDefaultWorkingDirPicked(string path)
    {
        browsingDefaultDir = false;

        OnDefaultWorkingDirChanged(path);
    }

    // Model and effort are agent-specific, so switching agent drops values the new one may not
    // offer rather than leaving a default that would be rejected at launch.
    private void OnDefaultAgentChanged(AgentType agent) => SaveDefaults(() =>
    {
        defaults.Agent = agent;
        defaults.Model = null;
        defaults.Effort = null;

        var offered = capabilities.For(agent).PermissionModes;

        if (!offered.Contains(defaults.PermissionMode))
            defaults.PermissionMode = offered.Count > 0 ? offered[0] : PermissionMode.Default;
    });

    private void OnDefaultModelChanged(string? model) => SaveDefaults(() =>
    {
        defaults.Model = model;

        if (defaults.Effort is { } effort && !capabilities.For(defaults.Agent).EffortsFor(model).Contains(effort))
            defaults.Effort = null;
    });

    private void OnDefaultEffortChanged(string? effort) => SaveDefaults(() => defaults.Effort = effort);

    private void OnDefaultPermissionChanged(PermissionMode mode) => SaveDefaults(() => defaults.PermissionMode = mode);

    private void OnDefaultScheduleChanged(TaskSchedule schedule) => SaveDefaults(() => defaults.Schedule = schedule);

    private void OnDefaultGitActionChanged(GitAction? action) => SaveDefaults(() => defaults.GitAction = action);

    private void SaveDefaults(Action change)
    {
        change();

        settings.SetTaskDefaults(defaults);
    }

    // On `Change` rather than on every keystroke: this writes to LiteDB, and a path is typed one
    // character at a time.
    // One picker at a time, keyed by agent: two open at once would be two file trees on a settings
    // page, and only one of them can be the one you meant.
    private AgentType? browsing;

    private void Browse(AgentDefaultsForm agent)
        => browsing = browsing == agent.Agent ? null : agent.Agent;

    // An empty box has no path to start from, so the walk begins wherever discovery says the CLI
    // is — which is exactly the folder someone hunting for it wants to be in.
    private static string? PickerStart(AgentDefaultsForm agent)
        => string.IsNullOrWhiteSpace(agent.Binary) ? agent.State.DiscoveredPath : agent.Binary;

    private void OnBinaryPicked(AgentDefaultsForm agent, string path)
    {
        browsing = null;

        OnBinaryChanged(agent, path);
    }

    private void OnBinaryChanged(AgentDefaultsForm agent, string value)
    {
        agent.Binary = value;
        agent.State = AgentBinaryCheck.For(probe, Adapter(agent.Agent), value);

        settings.SetDefaults(agent.ToDefaults());
    }

    private IAgentAdapter Adapter(AgentType agent) => adapters.First(candidate => candidate.Agent == agent);

    private void OnExtraFlagsChanged(AgentDefaultsForm agent, string value)
    {
        agent.ExtraFlags = value;

        settings.SetDefaults(agent.ToDefaults());
    }

    private void OnEnvChanged(AgentDefaultsForm agent, string value)
    {
        agent.Env = value;

        settings.SetDefaults(agent.ToDefaults());
    }

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

    private void OnBlinkYourTurnChanged(bool blink) => settings.SetBlinkYourTurn(blink);

    private void OnKeepAwakeChanged(bool keepAwake) => settings.SetKeepAwake(keepAwake);

    private void OnNotificationsChanged(bool notifications) => settings.SetNotifications(notifications);

    private void OnCloseToTrayChanged(bool closeToTray) => settings.SetCloseToTray(closeToTray);

    private void OnPreventConcurrentWorkingDirChanged(bool prevent)
        => settings.SetPreventConcurrentWorkingDir(prevent);

    private void OnAutoExecutionPausedChanged(bool paused) => settings.SetAutoExecutionPaused(paused);

    private void OnMaxConcurrentChanged(int cap) => settings.SetMaxConcurrent(cap);

    private void OnAutoArchiveCompletedChanged(bool autoArchive) => settings.SetAutoArchiveCompleted(autoArchive);

    private void OnAutoArchiveDaysChanged(int days) => settings.SetAutoArchiveCompletedAfterDays(days);

    private void BackToBoard() => navigation.NavigateTo("/");
}
