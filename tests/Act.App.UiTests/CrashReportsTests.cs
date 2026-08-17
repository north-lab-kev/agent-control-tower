using Act.App.Telemetry;
using Act.Core.Telemetry;
using AwesomeAssertions;
using Microsoft.JSInterop;

namespace Act.App.UiTests;

// The funnel every crash goes through, and the only place that decides what is worth sending. See
// *A disconnected circuit is not a crash* in `docs/design-notes.md` for why one exception type is
// refused here rather than caught at the call site.
public class CrashReportsTests
{
    [Fact]
    public void An_ordinary_failure_is_reported()
    {
        var (reports, transport) = ReportsOf();

        reports.Report(Thrown(new InvalidOperationException("the duplicate button threw")), fatal: false);

        transport.Named(TelemetryEvents.Names.AppError).Should().ContainSingle();
    }

    [Fact]
    public void A_fatal_failure_flushes_the_queue()
    {
        var (reports, transport) = ReportsOf();

        reports.Report(Thrown(new InvalidOperationException("terminating")), fatal: true);

        transport.Flushes.Should().Be(1);
    }

    [Fact]
    public void A_disconnected_circuit_is_not_reported()
    {
        var (reports, transport) = ReportsOf();

        reports.Report(Thrown(Disconnected()), fatal: false);

        transport.Captured.Should().BeEmpty();
    }

    [Fact]
    public void A_disconnected_circuit_wrapped_by_an_unobserved_task_is_not_reported()
    {
        var (reports, transport) = ReportsOf();

        reports.Report(new AggregateException(Thrown(Disconnected())), fatal: false);

        transport.Captured.Should().BeEmpty();
    }

    [Fact]
    public void A_disconnected_circuit_reported_as_fatal_neither_sends_nor_flushes()
    {
        var (reports, transport) = ReportsOf();

        reports.Report(Thrown(Disconnected()), fatal: true);

        transport.Captured.Should().BeEmpty();
        transport.Flushes.Should().Be(0);
    }

    [Fact]
    public void A_burst_of_disconnected_circuits_does_not_spend_the_cap()
    {
        var (reports, transport) = ReportsOf();

        for (var attempt = 0; attempt < 40; attempt++)
            reports.Report(new AggregateException(Thrown(Disconnected())), fatal: false);

        reports.Report(Thrown(new InvalidOperationException("the real one")), fatal: false);

        transport.Named(TelemetryEvents.Names.AppError).Should().ContainSingle().Subject
            .Properties[TelemetryProperties.Exception].Should().Be("System.InvalidOperationException");
    }

    [Fact]
    public void A_javascript_error_from_a_live_circuit_is_still_reported()
    {
        var (reports, transport) = ReportsOf();

        reports.Report(Thrown(new JSException("act-terminal.js: cols is not a number")), fatal: false);

        transport.Named(TelemetryEvents.Names.AppError).Should().ContainSingle().Subject
            .Properties[TelemetryProperties.Exception].Should().Be("Microsoft.JSInterop.JSException");
    }

    [Fact]
    public void A_failure_that_merely_wraps_something_disconnected_deeper_is_judged_by_the_innermost()
    {
        var (reports, transport) = ReportsOf();

        reports.Report(new InvalidOperationException("outer", Thrown(Disconnected())), fatal: false);

        transport.Captured.Should().BeEmpty();
    }

    private static JSDisconnectedException Disconnected()
        => new("JavaScript interop calls cannot be issued at this time.");

    private static (CrashReports, RecordingTelemetrySink) ReportsOf()
    {
        var transport = new RecordingTelemetrySink();

        return (new CrashReports(transport), transport);
    }

    private static TException Thrown<TException>(TException error)
        where TException : Exception
    {
        try
        {
            throw error;
        }
        catch (TException caught)
        {
            return caught;
        }
    }
}
