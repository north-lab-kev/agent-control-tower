using Act.App.Settings;
using Act.App.Telemetry;
using Act.Core.Model;
using Act.Core.Telemetry;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// The opt-out is the only thing between a call site and the network, so what is pinned here is that
// it is read per capture rather than per run: off stops the next event, and on resumes without a
// restart.
public class TelemetryConsentTests
{
    [Fact]
    public void Consent_on_by_default_lets_an_event_through()
    {
        var (sink, transport, _) = SinkOf();

        sink.Capture(Started("0.4.1"));

        transport.Captured.Should().HaveCount(1);
    }

    [Fact]
    public void Turning_it_off_stops_the_very_next_event()
    {
        var (sink, transport, settings) = SinkOf();

        settings.SetTelemetry(false);
        sink.Capture(Started("0.4.1"));

        transport.Captured.Should().BeEmpty();
    }

    [Fact]
    public void Turning_it_off_stops_the_flush_as_well()
    {
        var (sink, transport, settings) = SinkOf();

        settings.SetTelemetry(false);
        sink.FlushAsync().GetAwaiter().GetResult();

        transport.Flushes.Should().Be(0);
    }

    // The reason the switch is read on every capture rather than at startup: read once, this would
    // need a restart, and a user who opts back in should not have to find that out.
    [Fact]
    public void Turning_it_back_on_resumes_without_a_restart()
    {
        var (sink, transport, settings) = SinkOf();

        settings.SetTelemetry(false);
        sink.Capture(Started("0.4.1"));

        settings.SetTelemetry(true);
        sink.Capture(Started("0.4.2"));

        transport.Captured.Should().HaveCount(1);
        transport.Captured[0].Properties[TelemetryProperties.AppVersion].Should().Be("0.4.2");
    }

    // One event for the whole startup, settings included — the quota is counted in events.
    [Fact]
    public void A_startup_reports_the_run_and_its_settings_as_one_event()
    {
        var (_, transport, settings) = SinkOf();
        var pump = Pump(transport, settings);

        pump.Start();

        var started = transport.Captured.Should().ContainSingle().Subject;

        started.Name.Should().Be(TelemetryEvents.Names.AppStarted);
        started.Properties.Should().ContainKey(TelemetryProperties.AppVersion);
        started.Properties[TelemetryProperties.MaxConcurrent].Should().Be(settings.MaxConcurrent);
    }

    // Shutdown sends nothing of its own — it exists to push what the run already queued, which a
    // batch that never left would otherwise take with it.
    [Fact]
    public void A_shutdown_flushes_without_reporting_anything_new()
    {
        var (_, transport, settings) = SinkOf();
        var pump = Pump(transport, settings);

        pump.Start();

        var queued = transport.Captured.Count;

        pump.DisposeAsync().GetAwaiter().GetResult();

        transport.Captured.Should().HaveCount(queued);
        transport.Flushes.Should().Be(1);
    }

    [Fact]
    public void A_pump_disposed_twice_flushes_once()
    {
        var (_, transport, settings) = SinkOf();
        var pump = Pump(transport, settings);

        pump.Start();
        pump.DisposeAsync().GetAwaiter().GetResult();
        pump.DisposeAsync().GetAwaiter().GetResult();

        transport.Flushes.Should().Be(1);
    }

    private static TelemetryEvent Started(string version)
        => TelemetryEvents.AppStarted(version, "Microsoft Windows NT 10.0.26200.0", "en-CA", new UserSettings());

    private static TelemetryPump Pump(RecordingTelemetrySink transport, UserSettingsService settings)
        => new(new ConsentedTelemetrySink(transport, settings), settings);

    private static (ConsentedTelemetrySink, RecordingTelemetrySink, UserSettingsService) SinkOf()
    {
        var transport = new RecordingTelemetrySink();
        var settings = new UserSettingsService(new FakeSettingsStore(), new AppCulture());

        return (new ConsentedTelemetrySink(transport, settings), transport, settings);
    }
}
