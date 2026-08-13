using Act.App.Cards;
using Act.App.Desktop;
using Act.App.Hosting;
using Act.App.Resources;
using Act.App.Settings;
using Act.App.Updates;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Infrastructure.Logging;
using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Pages;

// Every setting applies and persists the moment it changes, so there is no Save and no Cancel —
// the only action is leaving, which is what the back arrow is for.
public partial class SettingsView(
    UserSettingsService settings,
    IEnumerable<IAgentAdapter> adapters,
    IExecutableProbe probe,
    IDesktopBridge desktop,
    UpdateState updateState,
    UpdatePump updates,
    ActLogLocation logs,
    ILogger<SettingsView> log,
    NavigationManager navigation) : IDisposable
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
        new(BoardDensity.Detailed, Strings.Settings_Density_Detailed),
        new(BoardDensity.Compact, Strings.Settings_Density_Compact),
    ];

    private LanguagePreference Language => settings.Language;

    private ThemePreference Theme => settings.Theme;

    private BoardDensity Density => settings.Density;

    private bool BlinkYourTurn => settings.BlinkYourTurn;

    private bool KeepAwake => settings.KeepAwake;

    private bool Notifications => settings.Notifications;

    private bool CloseToTray => settings.CloseToTray;

    private bool GenerateTitles => settings.GenerateTitles;

    private bool Telemetry => settings.Telemetry;

    private string InstallId => settings.InstallId;

    private bool PreventConcurrentWorkingDir => settings.PreventConcurrentWorkingDir;

    private bool AutoExecutionPaused => settings.AutoExecutionPaused;

    private int MaxConcurrent => settings.MaxConcurrent;

    private int MaxFollowUpsPerCard => settings.MaxFollowUpsPerCard;

    private int UsageCeilingPercent => settings.UsageCeilingPercent;

    private bool IsDesktop => desktop.IsDesktop;

    private static string Version => AppVersion.Current;

    private bool AutoUpdate => settings.AutoUpdate;

    private UpdateStatus UpdateStatus => updateState.Current;

    // Offered only where it is the answer to what the page is showing: a manual check found a version
    // and nothing is going to fetch it on its own. Without it, that check names a version and leaves no
    // way to have it.
    private bool CanDownloadUpdate => UpdateStatus.Stage is UpdateStage.Available && !AutoUpdate;

    // Offered whichever way the toggle sits, because by this point the choice it governed — whether to
    // go and fetch it — has already been made, and what is left is a version sitting on disk waiting
    // for a restart that only the user can time.
    private bool CanRestartForUpdate => UpdateStatus.Stage is UpdateStage.Ready;

    private string UpdateSummary => UpdateStatus.Stage switch
    {
        UpdateStage.Checking => Strings.Settings_Updates_Checking,
        UpdateStage.UpToDate => Strings.Settings_Updates_UpToDate,
        UpdateStage.Available => Text.Format(Strings.Settings_Updates_Available, UpdateStatus.Version),
        UpdateStage.Downloading => Text.Format(
            Strings.Settings_Updates_Downloading,
            UpdateStatus.Version,
            UpdateStatus.Percent),
        UpdateStage.Ready => Text.Format(Strings.Settings_Updates_Ready, UpdateStatus.Version),
        UpdateStage.Unavailable => Strings.Settings_Updates_Unavailable,

        // Nothing has been asked yet. With the toggle off that is the resting state rather than a
        // moment, and "Not checking for updates" beside a live *Check now* reads as a broken button.
        _ => AutoUpdate ? Strings.Settings_Updates_Idle : Strings.Settings_Updates_Manual,
    };

    private bool AutoArchiveCompleted => settings.AutoArchiveCompleted;

    private int AutoArchiveCompletedAfterDays => settings.AutoArchiveCompletedAfterDays;

    private static int MinimumRetentionDays => CompletedRetention.MinimumDays;

    private static int MaximumRetentionDays => CompletedRetention.MaximumDays;

    // Every registered adapter gets a section, in the order they were registered — the settings
    // model holds a list rather than a property per agent for the same reason.
    private List<AgentDefaultsForm> AgentForms { get; } = [];

    private static string AgentName(AgentType agent) => TaskLabels.Agent(agent);

    protected override void OnInitialized()
    {
        updateState.Changed += OnUpdateStateChanged;

        foreach (var adapter in adapters)
        {
            var form = AgentDefaultsForm.From(settings.Defaults(adapter.Agent));

            form.State = AgentBinaryCheck.For(probe, adapter, form.Binary);

            AgentForms.Add(form);
        }
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

    private void OpenTemplates() => navigation.NavigateTo("/templates");

    private string LogDirectory => logs.Directory;

    private bool logsFailed;

    private async Task OpenLogFolder()
    {
        try
        {
            // The bridge now *reports* a refusal instead of throwing it — a path the OS will not open is
            // ordinary rather than exceptional — so the returned message is what decides. The catch is
            // left for interop failing outright, which is a different thing.
            if (await desktop.OpenPathAsync(logs.Directory) is { } refused)
            {
                logsFailed = true;

                log.LogWarning(
                    "Opening the log folder {Directory} failed: {Refusal}",
                    logs.Directory,
                    refused);

                return;
            }

            logsFailed = false;
        }
        catch (Exception error)
        {
            logsFailed = true;

            log.LogWarning(error, "Opening the log folder {Directory} failed.", logs.Directory);
        }
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

    private void OnGenerateTitlesChanged(bool generate) => settings.SetGenerateTitles(generate);

    private void OnTelemetryChanged(bool telemetry) => settings.SetTelemetry(telemetry);

    private void OnPreventConcurrentWorkingDirChanged(bool prevent)
        => settings.SetPreventConcurrentWorkingDir(prevent);

    private void OnAutoExecutionPausedChanged(bool paused) => settings.SetAutoExecutionPaused(paused);

    private void OnMaxConcurrentChanged(int cap) => settings.SetMaxConcurrent(cap);

    private void OnMaxFollowUpsChanged(int cap) => settings.SetMaxFollowUpsPerCard(cap);

    private void OnUsageCeilingChanged(int percent) => settings.SetUsageCeilingPercent(percent);

    private void OnAutoArchiveCompletedChanged(bool autoArchive) => settings.SetAutoArchiveCompleted(autoArchive);

    private void OnAutoArchiveDaysChanged(int days) => settings.SetAutoArchiveCompletedAfterDays(days);

    private void OnAutoUpdateChanged(bool autoUpdate) => settings.SetAutoUpdate(autoUpdate);

    private void CheckForUpdate() => updates.CheckNow();

    private void DownloadUpdate() => updates.DownloadNow();

    private Task RestartForUpdate() => desktop.RestartForUpdateAsync();

    // The pump runs on its own thread, so a download's progress arrives from outside the renderer.
    private void OnUpdateStateChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => updateState.Changed -= OnUpdateStateChanged;

    private void BackToBoard() => navigation.NavigateTo("/");
}
