using Act.App.Settings;
using Act.App.Telemetry;
using Act.Core.Telemetry;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;

namespace Act.App.UiTests;

// A Blazor event handler is caught by `RemoteRenderer`/`CircuitHost`, an HTTP request by ASP.NET's
// middleware, and neither raises `AppDomain.UnhandledException` — which is why subscribing to that
// event reported nothing for a throwing button. All four layers end at an `ILogger` carrying the
// exception, so that is where the bridge listens.
public class TelemetryErrorBridgeTests
{
    [Fact]
    public void An_exception_logged_by_any_category_becomes_a_crash_report()
    {
        var (log, transport) = BridgeOf("Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost");

        log.LogError(Thrown("the duplicate button threw"), "Unhandled exception in circuit {Id}.", "abc");

        var reported = transport.Named(TelemetryEvents.Names.AppError).Should().ContainSingle().Subject;

        reported.Properties[TelemetryProperties.Exception].Should().Be("System.InvalidOperationException");
        reported.Properties[TelemetryProperties.Fatal].Should().Be(false);
    }

    [Fact]
    public void A_critical_is_reported_as_fatal()
    {
        var (log, transport) = BridgeOf("Act.App.Hosting.StartupLog");

        log.LogCritical(Thrown("terminating"), "Unhandled exception. Terminating: {Terminating}.", true);

        transport.Named(TelemetryEvents.Names.AppError).Single()
            .Properties[TelemetryProperties.Fatal].Should().Be(true);
    }

    // The line that separates this from telemetry-over-logging: only the exception is read. The
    // message is where ACT's paths and localised sentences live, and it never leaves.
    [Fact]
    public void The_log_message_never_reaches_the_payload()
    {
        var (log, transport) = BridgeOf("Act.App.Sessions.SessionLauncher");

        log.LogError(
            Thrown("boom"),
            "Launch failed in {WorkingDir} for {Title}.",
            @"C:\Users\someone\private-project",
            "Rename the widget");

        var values = transport.Named(TelemetryEvents.Names.AppError).Single().Properties.Values
            .SelectMany(value => value is IEnumerable<string> many ? many : [value?.ToString() ?? string.Empty]);

        values.Should().NotContain(value => value.Contains("private-project", StringComparison.Ordinal));
        values.Should().NotContain(value => value.Contains("Rename the widget", StringComparison.Ordinal));
    }

    [Fact]
    public void An_error_without_an_exception_is_not_a_crash_report()
    {
        var (log, transport) = BridgeOf("Act.App.Sessions.QueueRunner");

        log.LogError("Nothing launched: {Reason}.", "the folder is busy");

        transport.Captured.Should().BeEmpty();
    }

    [Theory]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Debug)]
    public void Anything_below_error_is_left_to_the_log_file(LogLevel level)
    {
        var (log, transport) = BridgeOf("Act.App.Sessions.TranscriptPump");

        log.Log(level, Thrown("transient"), "Transcript read was skipped.");

        transport.Captured.Should().BeEmpty();
    }

    // A crash loop must not become a send loop.
    [Fact]
    public void A_storm_of_failures_is_capped()
    {
        var (log, transport) = BridgeOf("Act.App.Hosting.BackgroundWork");

        for (var attempt = 0; attempt < 40; attempt++)
            log.LogError(Thrown($"pass {attempt}"), "A pass failed.");

        transport.Captured.Should().HaveCountLessThan(40);
        transport.Captured.Should().NotBeEmpty();
    }

    private static (ILogger, RecordingTelemetrySink) BridgeOf(string category)
    {
        var transport = new RecordingTelemetrySink();
        var settings = new UserSettingsService(new FakeSettingsStore(), new AppCulture());
        var reports = new CrashReports(new ConsentedTelemetrySink(transport, settings));

        using var bridge = new TelemetryErrorBridge(() => reports);

        return (bridge.CreateLogger(category), transport);
    }

    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException error)
        {
            return error;
        }
    }
}
