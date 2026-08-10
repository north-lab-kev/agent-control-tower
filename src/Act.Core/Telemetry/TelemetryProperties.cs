namespace Act.Core.Telemetry;

public static class TelemetryProperties
{
    public const string AppVersion = "app_version";

    public const string OperatingSystem = "os";

    public const string Locale = "locale";

    public const string Exception = "exception";

    public const string ExceptionChain = "exception_chain";

    public const string Frames = "frames";

    public const string Fatal = "fatal";

    public const string Language = "language";

    public const string Theme = "theme";

    public const string Density = "density";

    public const string BlinkYourTurn = "blink_your_turn";

    public const string KeepAwake = "keep_awake";

    public const string Notifications = "notifications";

    public const string CloseToTray = "close_to_tray";

    public const string Updates = "updates";

    public const string PreventConcurrentWorkingDir = "prevent_concurrent_working_dir";

    public const string MaxConcurrent = "max_concurrent";

    public const string AutoExecutionPaused = "auto_execution_paused";

    public const string UsageCeilingPercent = "usage_ceiling_percent";

    public const string AutoArchiveCompleted = "auto_archive_completed";

    public const string AutoArchiveAfterDays = "auto_archive_after_days";

    public const string TemplateCount = "template_count";

    public const string EnabledAgents = "enabled_agents";

    public const string Agent = "agent";

    public const string Origin = "origin";

    public const string AutoGit = "auto_git";

    public const string Scheduled = "scheduled";

    public const string AttachmentCount = "attachment_count";

    public const string DependencyCount = "dependency_count";

    public static readonly string[] All =
    [
        AppVersion,
        OperatingSystem,
        Locale,
        Exception,
        ExceptionChain,
        Frames,
        Fatal,
        Language,
        Theme,
        Density,
        BlinkYourTurn,
        KeepAwake,
        Notifications,
        CloseToTray,
        Updates,
        PreventConcurrentWorkingDir,
        MaxConcurrent,
        AutoExecutionPaused,
        UsageCeilingPercent,
        AutoArchiveCompleted,
        AutoArchiveAfterDays,
        TemplateCount,
        EnabledAgents,
        Agent,
        Origin,
        AutoGit,
        Scheduled,
        AttachmentCount,
        DependencyCount,
    ];
}
