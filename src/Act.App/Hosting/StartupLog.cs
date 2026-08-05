using System.Reflection;
using Act.Core.Abstractions;
using Act.Infrastructure.Logging;

namespace Act.App.Hosting;

public static class StartupLog
{
    public static void Report(WebApplication app, string dataDirectory, bool desktop)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StartupLog));

        log.LogInformation(
            "ACT {Version} starting as {Mode} in {Environment}.",
            Version(),
            desktop ? "the desktop shell" : "a browser app",
            app.Environment.EnvironmentName);

        log.LogInformation(
            "Data directory {DataDirectory}; logs {LogDirectory}.",
            dataDirectory,
            app.Services.GetRequiredService<ActLogLocation>().Directory);

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            if (app.Services.GetRequiredService<IHookEndpoint>().BaseAddress is { } hooks)
                log.LogInformation("Hooks answer on {HookEndpoint}.", hooks);
            else
                log.LogWarning("The hook endpoint is not listening; agents will run without hooks.");
        });

        app.Lifetime.ApplicationStopping.Register(() => log.LogInformation("ACT is stopping."));
        app.Lifetime.ApplicationStopped.Register(() => log.LogInformation("ACT has stopped."));

        WatchForUnhandled(log);
    }

    private static string Version()
        => typeof(StartupLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

    // The last resort, and the only reason a crash leaves anything in the file at all: a background
    // loop that escapes `BackgroundWork`, or a shutdown path that throws, has no other reporter.
    private static void WatchForUnhandled(ILogger log)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, unhandled) => log.LogCritical(
            unhandled.ExceptionObject as Exception,
            "Unhandled exception. Terminating: {Terminating}.",
            unhandled.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, faulted) =>
        {
            log.LogError(faulted.Exception, "A task faulted with nobody awaiting it.");

            faulted.SetObserved();
        };
    }
}
