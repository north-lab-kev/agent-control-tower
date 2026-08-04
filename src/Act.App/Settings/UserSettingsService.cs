using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Core.Scheduling;

namespace Act.App.Settings;

public sealed class UserSettingsService(ISettingsStore store, AppCulture culture)
{
    private readonly Lock gate = new();

    private readonly UserSettings current = store.Load();

    public event Action? Changed;

    public LanguagePreference Language => current.Language;

    public ThemePreference Theme => current.Theme;

    public BoardDensity Density => current.Density;

    public bool BlinkYourTurn => current.BlinkYourTurn;

    public bool KeepAwake => current.KeepAwake;

    public bool Notifications => current.Notifications;

    public bool CloseToTray => current.CloseToTray;

    public bool PreventConcurrentWorkingDir => current.PreventConcurrentWorkingDir;

    public int MaxConcurrent => ConcurrencySlots.Clamp(current.MaxConcurrent);

    public bool AutoExecutionPaused => current.AutoExecutionPaused;

    public int UsageCeilingPercent => UsageCeiling.Clamp(current.UsageCeilingPercent);

    // Everything the queue reads, in one object, so the runner and the board ask the same question
    // of the same values rather than each assembling their own.
    public QueuePolicy QueuePolicy => new(
        MaxConcurrent,
        current.AutoExecutionPaused,
        current.PreventConcurrentWorkingDir,
        EnabledAgents.ToHashSet(),
        UsageCeilingPercent);

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

    public WindowBounds? Window => current.Window?.Copy();

    // Straight to the store rather than through `Update`: dragging a window edge fires this many
    // times a second, and nothing on screen is bound to it.
    public void SetWindow(WindowBounds bounds)
    {
        lock (gate)
        {
            current.Window = bounds.Copy();
            store.Save(current);
        }
    }

    public bool AgentInstallsProbed => current.AgentInstallsProbed;

    public void MarkAgentInstallsProbed() => Update(settings => settings.AgentInstallsProbed = true);

    private AgentDefaults? Stored(AgentType agent)
        => current.Agents.FirstOrDefault(entry => entry.Agent == agent);

    public void ApplyLanguage() => culture.Apply(current.Language);

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

    public void SetNotifications(bool notifications)
    {
        if (notifications == current.Notifications)
            return;

        Update(settings => settings.Notifications = notifications);
    }

    public void SetCloseToTray(bool closeToTray)
    {
        if (closeToTray == current.CloseToTray)
            return;

        Update(settings => settings.CloseToTray = closeToTray);
    }

    public void SetPreventConcurrentWorkingDir(bool prevent)
    {
        if (prevent == current.PreventConcurrentWorkingDir)
            return;

        Update(settings => settings.PreventConcurrentWorkingDir = prevent);
    }

    public void SetMaxConcurrent(int cap)
    {
        var clamped = ConcurrencySlots.Clamp(cap);

        if (clamped == current.MaxConcurrent)
            return;

        Update(settings => settings.MaxConcurrent = clamped);
    }

    public void SetUsageCeilingPercent(int percent)
    {
        var clamped = UsageCeiling.Clamp(percent);

        if (clamped == current.UsageCeilingPercent)
            return;

        Update(settings => settings.UsageCeilingPercent = clamped);
    }

    public void SetAutoExecutionPaused(bool paused)
    {
        if (paused == current.AutoExecutionPaused)
            return;

        Update(settings => settings.AutoExecutionPaused = paused);
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
        Changed?.Invoke();
    }
}
