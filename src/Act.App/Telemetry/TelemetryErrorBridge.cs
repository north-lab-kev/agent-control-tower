namespace Act.App.Telemetry;

// How a crash reaches telemetry, and the only way it does.
//
// ACT catches unhandled exceptions in four places — see *Four layers catch an unhandled exception* in
// `docs/design-notes.md` — and only two of them are `AppDomain`/`TaskScheduler` events. **A Blazor
// component or event handler is caught by `RemoteRenderer` and `CircuitHost`, which is most of them in
// this app**, and a failed HTTP request by ASP.NET's middleware. Subscribing to the two events missed
// every UI exception there is. What all four have in common is that they end at an `ILogger` carrying
// the exception object, so that is what this listens to.
//
// **This is not telemetry-over-logging.** The objection to that shape was that every call site becomes
// a payload — see *Telemetry is not a log* in the design notes. Nothing of the record is read here
// except `Exception`: never the message, never the state, never the formatter, so the localised
// sentences and paths that make ACT's log useful cannot reach the wire. The event is still built by
// `TelemetryEvents.AppError` and still sanitised by `TelemetryPayload`.
public sealed class TelemetryErrorBridge(Func<CrashReports> reports) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Forwarder(reports);

    public void Dispose()
    {
    }

    private sealed class Forwarder(Func<CrashReports> reports) : ILogger
    {
        // Guards the one failure that would be worse than losing the report: resolving the sink can
        // itself log, and a throw on that path would re-enter here and recurse until the stack ends.
        [ThreadStatic]
        private static bool reporting;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // An error worth logging with an exception is a crash report; an error without one is a
            // sentence, and a sentence is exactly what must not leave.
            if (exception is null || logLevel < LogLevel.Error || reporting)
                return;

            reporting = true;

            try
            {
                reports().Report(exception, logLevel is LogLevel.Critical);
            }
            catch
            {
                // Reporting a crash must never become one.
            }
            finally
            {
                reporting = false;
            }
        }
    }
}
