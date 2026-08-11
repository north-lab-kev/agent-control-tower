using Act.Core.Model;

namespace Act.Core.Telemetry;

// Every payload ACT can send, built here and nowhere else. A call site names an event and hands over
// typed values; it never assembles a property bag of its own, which is what keeps "no prompts, no
// task text, no paths, no agent output" reviewable in one file instead of at every `Capture`.
//
// The factories that take a `Card` or a `UserSettings` read only counts, flags and enum names off
// them. Those objects carry the prompt, the title, the working directory and each agent's binary
// path — none of which appears below, and `TelemetryPayload` would drop them if it did.
//
// **The scope is the app, not the work.** Three events: the run starting with the settings it ran
// under, what a launch looked like, and the crashes. Deliberately absent: a `task_completed` (turn
// counts and token totals measure the agent's work rather than the app's use of it), an
// `agent_discovered` (its one useful fact `enabled_agents` already carries), an `app_stopped`
// uptime (nobody would act on it), and a separate `settings_snapshot` (folded into `AppStarted`,
// because two events cost twice the quota to say one thing). Prefer removing an event to adding
// one: every payload here is something a user has to be willing to send, and the meter counts
// events, not bytes.
public static class TelemetryEvents
{
    public static class Names
    {
        public const string AppStarted = "app_started";

        public const string AppError = "app_error";

        public const string TaskLaunched = "task_launched";

        public static readonly string[] All =
        [
            AppStarted,
            AppError,
            TaskLaunched,
        ];
    }

    // The run and the configuration it ran under, in one event rather than two. What meters an
    // analytics quota is events ingested, not payload size, so a second event carrying the settings
    // would double what a startup costs to say the same thing — and having the settings *on* the start
    // means a run can be broken down by any of them without joining two events together.
    public static TelemetryEvent AppStarted(
        string appVersion,
        string operatingSystem,
        string locale,
        UserSettings settings)
        => new(Names.AppStarted, new Dictionary<string, object?>
        {
            [TelemetryProperties.AppVersion] = appVersion,
            [TelemetryProperties.OperatingSystem] = operatingSystem,
            [TelemetryProperties.Locale] = locale,
            [TelemetryProperties.Language] = settings.Language,
            [TelemetryProperties.Theme] = settings.Theme,
            [TelemetryProperties.Density] = settings.Density,
            [TelemetryProperties.BlinkYourTurn] = settings.BlinkYourTurn,
            [TelemetryProperties.KeepAwake] = settings.KeepAwake,
            [TelemetryProperties.Notifications] = settings.Notifications,
            [TelemetryProperties.CloseToTray] = settings.CloseToTray,
            [TelemetryProperties.Updates] = settings.Updates,
            [TelemetryProperties.PreventConcurrentWorkingDir] = settings.PreventConcurrentWorkingDir,
            [TelemetryProperties.MaxConcurrent] = settings.MaxConcurrent,
            [TelemetryProperties.AutoExecutionPaused] = settings.AutoExecutionPaused,
            [TelemetryProperties.UsageCeilingPercent] = settings.UsageCeilingPercent,
            [TelemetryProperties.AutoArchiveCompleted] = settings.AutoArchiveCompleted,
            [TelemetryProperties.AutoArchiveAfterDays] = settings.AutoArchiveCompletedAfterDays,
            [TelemetryProperties.TemplateCount] = settings.Templates.Count,
            [TelemetryProperties.EnabledAgents] = Enabled(settings),
        });

    // `exception` is the *innermost* type, because that is the one that failed: an unobserved task
    // hands the handler an `AggregateException`, and grouping every distinct fault under that name is
    // no better than not reporting it. The wrapper is still visible in `exception_chain`.
    public static TelemetryEvent AppError(Exception error, bool fatal)
    {
        var chain = TelemetryFault.Chain(error);

        return new(Names.AppError, new Dictionary<string, object?>
        {
            [TelemetryProperties.Exception] = TelemetryFault.Failure(error).GetType().FullName,
            [TelemetryProperties.ExceptionChain] = chain.Length > 1 ? chain : null,
            [TelemetryProperties.Frames] = TelemetryFault.Frames(error),
            [TelemetryProperties.Fatal] = fatal,
        });
    }

    public static TelemetryEvent TaskLaunched(Card card)
        => new(Names.TaskLaunched, new Dictionary<string, object?>
        {
            [TelemetryProperties.Agent] = card.AgentType,
            [TelemetryProperties.Origin] = card.Origin,
            [TelemetryProperties.AutoGit] = card.AutoGit is not null,
            [TelemetryProperties.Scheduled] = card.Schedule is not null,
            [TelemetryProperties.AttachmentCount] = card.Attachments.Count,
            [TelemetryProperties.DependencyCount] = card.DependsOn.Count,
        });

    private static string[] Enabled(UserSettings settings)
        => [.. settings.Agents.Where(entry => entry.Enabled).Select(entry => entry.Agent.ToString())];
}
