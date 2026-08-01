using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;

namespace Act.App.Settings;

public sealed class UserSettingsService(ISettingsStore store, AppCulture culture, ISleepInhibitor sleep)
{
    private readonly Lock gate = new();

    private readonly UserSettings current = store.Load();

    public event Action? Changed;

    public LanguagePreference Language => current.Language;

    public ThemePreference Theme => current.Theme;

    public BoardDensity Density => current.Density;

    public bool BlinkYourTurn => current.BlinkYourTurn;

    public bool KeepAwake => current.KeepAwake;

    public bool CloseToTray => current.CloseToTray;

    public bool AutoArchiveCompleted => current.AutoArchiveCompleted;

    public int AutoArchiveCompletedAfterDays => CompletedRetention.ClampDays(current.AutoArchiveCompletedAfterDays);

    public TimeSpan? CompletedRetentionWindow => current.AutoArchiveCompleted
        ? CompletedRetention.Window(current.AutoArchiveCompletedAfterDays)
        : null;

    // A copy, always: the caller is a form binding its own fields, and handing out the stored object
    // would let it edit the settings in place and skip the save.
    public AgentDefaults Defaults(AgentType agent)
        => Stored(agent)?.Copy() ?? new AgentDefaults { Agent = agent };

    public void SetDefaults(AgentDefaults defaults)
    {
        Update(settings =>
        {
            var existing = settings.Agents.FirstOrDefault(entry => entry.Agent == defaults.Agent);

            if (existing is not null)
                settings.Agents.Remove(existing);

            settings.Agents.Add(defaults.Copy());
        });
    }

    // Only the agents whose switch is on, in registration order — what the task form offers.
    public IReadOnlyList<AgentType> EnabledAgents
        => [.. current.Agents.Where(entry => entry.Enabled).Select(entry => entry.Agent)];

    public TaskDefaults TaskDefaults => current.TaskDefaults.Copy();

    public void SetTaskDefaults(TaskDefaults defaults)
        => Update(settings => settings.TaskDefaults = defaults.Copy());

    // Startup only, for the one-time lift of what used to live on each card.
    public bool AgentDefaultsLifted => current.AgentDefaultsLifted;

    public void MarkAgentDefaultsLifted() => Update(settings => settings.AgentDefaultsLifted = true);

    public bool AgentInstallsProbed => current.AgentInstallsProbed;

    public void MarkAgentInstallsProbed() => Update(settings => settings.AgentInstallsProbed = true);

    private AgentDefaults? Stored(AgentType agent)
        => current.Agents.FirstOrDefault(entry => entry.Agent == agent);

    public void ApplyLanguage() => culture.Apply(current.Language);

    // Called at startup as well as on every change, because a setting that only takes effect when
    // you toggle it is a setting that quietly turns itself off every time the app restarts.
    public void ApplyKeepAwake()
    {
        if (current.KeepAwake)
            sleep.Hold();
        else
            sleep.Release();
    }

    public void SetLanguage(LanguagePreference language)
    {
        if (language == current.Language)
            return;

        Update(settings => settings.Language = language);
    }

    public void SetTheme(ThemePreference theme)
    {
        if (theme == current.Theme)
            return;

        Update(settings => settings.Theme = theme);
    }

    public void SetDensity(BoardDensity density)
    {
        if (density == current.Density)
            return;

        Update(settings => settings.Density = density);
    }

    public void SetBlinkYourTurn(bool blink)
    {
        if (blink == current.BlinkYourTurn)
            return;

        Update(settings => settings.BlinkYourTurn = blink);
    }

    public void SetKeepAwake(bool keepAwake)
    {
        if (keepAwake == current.KeepAwake)
            return;

        Update(settings => settings.KeepAwake = keepAwake);
    }

    public void SetCloseToTray(bool closeToTray)
    {
        if (closeToTray == current.CloseToTray)
            return;

        Update(settings => settings.CloseToTray = closeToTray);
    }

    public void SetAutoArchiveCompleted(bool autoArchive)
    {
        if (autoArchive == current.AutoArchiveCompleted)
            return;

        Update(settings => settings.AutoArchiveCompleted = autoArchive);
    }

    public void SetAutoArchiveCompletedAfterDays(int days)
    {
        var clamped = CompletedRetention.ClampDays(days);

        if (clamped == current.AutoArchiveCompletedAfterDays)
            return;

        Update(settings => settings.AutoArchiveCompletedAfterDays = clamped);
    }

    private void Update(Action<UserSettings> change)
    {
        lock (gate)
        {
            change(current);
            store.Save(current);
        }

        ApplyLanguage();
        ApplyKeepAwake();
        Changed?.Invoke();
    }
}
