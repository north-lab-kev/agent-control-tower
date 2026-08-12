using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Core.Scheduling;
using Act.Core.Spawning;
using Act.Core.Telemetry;

namespace Act.App.Settings;

public sealed class UserSettingsService(ISettingsStore store, AppCulture culture)
{
    private readonly Lock gate = new();

    private readonly UserSettings current = Seeded(store);

    public event Action? Changed;

    public LanguagePreference Language => current.Language;

    public ThemePreference Theme => current.Theme;

    public BoardDensity Density => current.Density;

    public bool BlinkYourTurn => current.BlinkYourTurn;

    public bool KeepAwake => current.KeepAwake;

    public bool Notifications => current.Notifications;

    public bool CloseToTray => current.CloseToTray;

    public bool GenerateTitles => current.GenerateTitles;

    public UpdatePolicy Updates => current.Updates;

    public bool Telemetry => current.Telemetry;

    public string InstallId => current.InstallId;

    // The startup event carries the settings, so it is assembled through here rather than in the pump:
    // `current` stays private, and the factory reads counts, flags and enum names off it and nothing
    // else — see `TelemetryEvents`.
    public TelemetryEvent TelemetryStarted(string appVersion, string operatingSystem, string locale)
        => TelemetryEvents.AppStarted(appVersion, operatingSystem, locale, current);

    public bool PreventConcurrentWorkingDir => current.PreventConcurrentWorkingDir;

    public int MaxConcurrent => ConcurrencySlots.Clamp(current.MaxConcurrent);

    public bool AutoExecutionPaused => current.AutoExecutionPaused;

    public int UsageCeilingPercent => UsageCeiling.Clamp(current.UsageCeilingPercent);

    public int MaxFollowUpsPerCard => SpawnQuota.Clamp(current.MaxFollowUpsPerCard);

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

    // Copies, always: a template is edited on a page of its own, and handing out the stored objects
    // would let that page write settings without saving them.
    //
    // Sorted by name rather than by creation, because this list is read to *find* a template — the
    // board's picker and the templates page both show it — and insertion order is an order only the
    // person who created them knows. By the *displayed* name, so the default sorts where the word the
    // user reads puts it rather than where its empty stored name would. Culture-aware and
    // case-insensitive, since the names are the user's own words in whichever language they work in.
    public IReadOnlyList<TaskTemplate> Templates
        => [.. current.Templates
            .OrderBy(TaskLabels.Template, StringComparer.CurrentCultureIgnoreCase)
            .Select(template => template.Copy())];

    public TaskTemplate DefaultTemplate => Default().Copy();

    public TaskTemplate? Template(Guid id) => Stored(id)?.Copy();

    // What `/card/new` starts from: the named template when there is one, the default when the id is
    // absent or names a template that has since been deleted.
    public TaskTemplate TemplateOrDefault(Guid? id)
        => (id is { } wanted ? Stored(wanted) : null)?.Copy() ?? DefaultTemplate;

    public void SaveTemplate(TaskTemplate template)
    {
        Update(settings =>
        {
            var saved = template.Copy();

            if (Stored(settings, saved.Id) is { } existing)
            {
                saved.IsDefault = existing.IsDefault;

                Restrict(saved);

                settings.Templates[settings.Templates.IndexOf(existing)] = saved;

                return;
            }

            if (saved.Id == Guid.Empty)
                saved.Id = Guid.NewGuid();

            saved.IsDefault = false;

            settings.Templates.Add(saved);
        });
    }

    // What the default template may not hold, enforced here rather than only by the page that hides
    // the three fields — the code that writes the store is the only place that can promise it.
    //
    // Its **name** is not stored, because it is not the user's to change and a stored translation
    // would freeze the language it was written in; `TaskLabels.Template` renders it. Its **title** and
    // **prompt** stay empty because this is what the bare New task button starts from: pre-filling
    // either one there means every task begins as a copy of the last, which is the whole reason the
    // settings block it replaced carried neither. A *named* template is asked for by name, so it may
    // carry both.
    private static void Restrict(TaskTemplate template)
    {
        if (!template.IsDefault)
            return;

        template.Name = string.Empty;
        template.Title = string.Empty;
        template.Prompt = string.Empty;
    }

    // Refuses the default rather than reporting it: nothing offers the action for that template, and
    // a store with no default is a board whose New task button has nothing to start from.
    public void DeleteTemplate(Guid id)
    {
        Update(settings =>
        {
            if (Stored(settings, id) is { IsDefault: false } template)
                settings.Templates.Remove(template);
        });
    }

    private TaskTemplate Default()
        => current.Templates.FirstOrDefault(template => template.IsDefault) ?? new TaskTemplate();

    private TaskTemplate? Stored(Guid id) => Stored(current, id);

    private static TaskTemplate? Stored(UserSettings settings, Guid id)
        => settings.Templates.FirstOrDefault(template => template.Id == id);

    // The invariants every reader leans on, seeded before anything can read a settings object without
    // them: exactly one default template, and an install id.
    //
    // A fresh install has no settings document at all, so neither can come from the store and neither
    // is a schema concern — a value generated when it is missing is a seed, not a second stored shape,
    // and after this runs there is no code path that has to ask whether it is there. It also
    // re-applies `Restrict`, which is what clears a name, title or prompt the default template must
    // never carry.
    private static UserSettings Seeded(ISettingsStore store)
    {
        var settings = store.Load();

        var dirty = false;

        // Random, never derived from the machine: the promise on the switch is that the data is
        // anonymous, and a hardware-derived id would tie every install of every user to one device.
        if (string.IsNullOrEmpty(settings.InstallId))
        {
            settings.InstallId = Guid.NewGuid().ToString();

            dirty = true;
        }

        if (!settings.Templates.Any(template => template.IsDefault))
        {
            if (settings.Templates.FirstOrDefault() is { } first)
                first.IsDefault = true;
            else
                settings.Templates.Add(new TaskTemplate { Id = Guid.NewGuid(), IsDefault = true });

            dirty = true;
        }

        foreach (var template in settings.Templates.Where(template => template.IsDefault))
        {
            dirty |= template.Name.Length > 0 || template.Title.Length > 0 || template.Prompt.Length > 0;

            Restrict(template);
        }

        if (dirty)
            store.Save(settings);

        return settings;
    }

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

    public string LastWorkingDir => current.LastWorkingDir;

    // Straight to the store like the window, and silent on an empty folder: a task created without
    // one is not a reason to forget the folder the previous one used.
    public void SetLastWorkingDir(string workingDir)
    {
        var folder = workingDir.Trim();

        if (folder.Length == 0 || folder == current.LastWorkingDir)
            return;

        lock (gate)
        {
            current.LastWorkingDir = folder;
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

    public void SetUpdates(UpdatePolicy updates)
    {
        if (updates == current.Updates)
            return;

        Update(settings => settings.Updates = updates);
    }

    public void SetCloseToTray(bool closeToTray)
    {
        if (closeToTray == current.CloseToTray)
            return;

        Update(settings => settings.CloseToTray = closeToTray);
    }

    public void SetGenerateTitles(bool generate)
    {
        if (generate == current.GenerateTitles)
            return;

        Update(settings => settings.GenerateTitles = generate);
    }

    public void SetTelemetry(bool telemetry)
    {
        if (telemetry == current.Telemetry)
            return;

        Update(settings => settings.Telemetry = telemetry);
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

    public void SetMaxFollowUpsPerCard(int cap)
    {
        var clamped = SpawnQuota.Clamp(cap);

        if (clamped == current.MaxFollowUpsPerCard)
            return;

        Update(settings => settings.MaxFollowUpsPerCard = clamped);
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
