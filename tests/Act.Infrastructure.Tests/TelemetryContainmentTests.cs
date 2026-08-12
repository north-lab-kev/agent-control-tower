using System.Runtime.CompilerServices;
using Act.Core.Telemetry;
using Act.Infrastructure.Telemetry;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PostHog;
using PostHog.Api;

namespace Act.Infrastructure.Tests;

// The same rule `ActLoggingTests` holds Serilog to, for the same reason: the vendor is a detail
// behind `ITelemetrySink`, and swapping it should be one folder rather than a search.
public class TelemetryContainmentTests
{
    [Fact]
    public void Only_the_telemetry_folder_names_posthog()
    {
        var source = SourceRoot();

        var offenders =
            from file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            let relative = Path.GetRelativePath(source, file)
            where !relative.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(Path.Combine("Act.Infrastructure", "Telemetry") + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            where File.ReadAllText(file).Contains("PostHog", StringComparison.OrdinalIgnoreCase)
            select relative;

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void The_telemetry_folder_is_where_it_actually_lives()
    {
        var wiring = Path.Combine(SourceRoot(), "Act.Infrastructure", "Telemetry", "ActTelemetry.cs");

        File.ReadAllText(wiring).Should().Contain("PostHog");
    }

    [Fact]
    public void A_build_with_no_token_gets_a_sink_that_sends_nothing()
    {
        var options = new TelemetryOptions { Enabled = true, ProjectToken = "  " };

        options.Configured.Should().BeFalse();
        ActTelemetry.Transport(EmptyProvider.Instance, options, "install").Should().BeOfType<NullTelemetrySink>();
    }

    [Fact]
    public void A_disabled_section_gets_a_sink_that_sends_nothing()
    {
        var options = new TelemetryOptions { Enabled = false, ProjectToken = "phc_realtoken" };

        options.Configured.Should().BeFalse();
        ActTelemetry.Transport(EmptyProvider.Instance, options, "install").Should().BeOfType<NullTelemetrySink>();
    }

    // Two ways of writing the key down, because the release stamps it base64 while a local run pastes
    // the real thing. `phc_` is what tells them apart — `_` is not in the base64 alphabet, so a plain
    // key can never be read as an encoded one.
    [Fact]
    public void A_plain_key_is_taken_as_written()
    {
        var options = new TelemetryOptions { ProjectToken = "phc_notarealtoken" };

        options.Token.Should().Be("phc_notarealtoken");
        options.Configured.Should().BeTrue();
    }

    [Fact]
    public void A_base64_key_is_decoded()
    {
        var options = new TelemetryOptions { ProjectToken = Encoded("phc_notarealtoken") };

        options.Token.Should().Be("phc_notarealtoken");
        options.Configured.Should().BeTrue();
    }

    [Fact]
    public void Surrounding_whitespace_does_not_stop_either_form_resolving()
    {
        new TelemetryOptions { ProjectToken = "  phc_notarealtoken\n" }.Token.Should().Be("phc_notarealtoken");
        new TelemetryOptions { ProjectToken = $" {Encoded("phc_notarealtoken")} " }.Token.Should().Be("phc_notarealtoken");
    }

    // Neither a key nor base64 of one: passing it on would fail every batch at the far end, which is
    // a worse answer than never starting a client.
    [Theory]
    [InlineData("not a token at all")]
    [InlineData("bm90IGEgdG9rZW4=")]
    [InlineData("phx_apersonalkey")]
    public void Anything_that_is_not_a_project_key_leaves_it_unconfigured(string written)
    {
        var options = new TelemetryOptions { ProjectToken = written };

        options.Token.Should().BeNull();
        options.Configured.Should().BeFalse();
    }

    // The whole release path in one test: what the pipeline writes into the shipped `appsettings.json`
    // has to come back out through the section the app binds. The pipeline's own read-back checks the
    // file; this checks that the file reaches `TelemetryOptions` — a renamed section or property would
    // pass there and fail here.
    [Fact]
    public void What_the_pipeline_stamps_is_what_the_app_binds()
    {
        var stamped = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("phc_notarealtoken"));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TelemetryOptions.SectionName}:Enabled"] = "true",
                [$"{TelemetryOptions.SectionName}:ProjectToken"] = stamped,
                [$"{TelemetryOptions.SectionName}:Host"] = "https://us.i.posthog.com",
            })
            .Build();

        var options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>();

        options!.Configured.Should().BeTrue();
        options.Token.Should().Be("phc_notarealtoken");
    }

    // The shipped default, which is what a fork or a build from source gets.
    [Fact]
    public void An_unstamped_build_binds_to_nothing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TelemetryOptions.SectionName}:Enabled"] = "true",
                [$"{TelemetryOptions.SectionName}:ProjectToken"] = string.Empty,
            })
            .Build();

        var options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>();

        options!.Configured.Should().BeFalse();
        ActTelemetry.Transport(EmptyProvider.Instance, options, "install").Should().BeOfType<NullTelemetrySink>();
    }

    [Fact]
    public void A_host_that_is_not_a_url_is_treated_as_unconfigured()
    {
        new TelemetryOptions { ProjectToken = "phc_realtoken", Host = "not a url" }.Configured.Should().BeFalse();
        new TelemetryOptions { ProjectToken = "phc_realtoken", Host = "file:///etc/passwd" }.Configured.Should().BeFalse();
    }

    [Fact]
    public void The_default_host_is_posthog_us()
    {
        new TelemetryOptions().HostUri.Should().Be(new Uri("https://us.i.posthog.com"));
    }

    // The registration the other tests cannot reach: with a token in hand the client has to be
    // resolvable, which is the half of the wiring that only fails at startup.
    [Fact]
    public void A_configured_token_resolves_a_real_transport()
    {
        var options = new TelemetryOptions { ProjectToken = Encoded("phc_notarealtoken") };
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddActTelemetryClient(options, _ => () => true);

        using var provider = services.BuildServiceProvider();

        options.Configured.Should().BeTrue();
        ActTelemetry.Transport(provider, options, "install").Should().BeOfType<PostHogTelemetrySink>();
    }

    // The shutdown crash, as a test. The consent gate runs on the client's send path,
    // and a run's last send happens as the app closes — after the container has been disposed. Asking
    // the provider there threw `ObjectDisposedException` out of the flush and took the closing batch
    // with it, so the predicate has to hold what it needs rather than go looking for it.
    [Fact]
    public void The_consent_gate_survives_the_container_it_was_built_from()
    {
        var options = new TelemetryOptions { ProjectToken = Encoded("phc_notarealtoken") };
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<Switch>();
        services.AddActTelemetryClient(options, provider =>
        {
            var consent = provider.GetRequiredService<Switch>();

            return () => consent.On;
        });

        var provider = services.BuildServiceProvider();
        var gate = provider.GetRequiredService<IOptions<PostHogOptions>>().Value.BeforeSend;

        gate.Should().NotBeNull();

        provider.Dispose();

        var sending = () => gate(Sample());

        sending.Should().NotThrow();
        gate(Sample()).Should().NotBeNull();
    }

    // And the gate still answers the question after that, rather than just not throwing.
    [Fact]
    public void A_withdrawn_consent_still_drops_a_queued_event_during_shutdown()
    {
        var options = new TelemetryOptions { ProjectToken = Encoded("phc_notarealtoken") };
        var services = new ServiceCollection();
        var consent = new Switch { On = false };

        services.AddLogging();
        services.AddSingleton(consent);
        services.AddActTelemetryClient(options, _ => () => consent.On);

        var provider = services.BuildServiceProvider();
        var gate = provider.GetRequiredService<IOptions<PostHogOptions>>().Value.BeforeSend;

        gate.Should().NotBeNull();

        provider.Dispose();

        gate(Sample()).Should().BeNull();

        consent.On = true;

        gate(Sample()).Should().NotBeNull();
    }

    // The belt under the fix, and what the crash would have been reduced to on its own: a gate that
    // reaches for a disposed provider drops the event instead of throwing out of the client's batch
    // send. Written the buggy way on purpose — it is also what proves the two tests above would fail
    // if the resolution moved back into the predicate.
    [Fact]
    public void A_gate_that_cannot_answer_drops_rather_than_throws()
    {
        var options = new TelemetryOptions { ProjectToken = Encoded("phc_notarealtoken") };
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<Switch>();
        services.AddActTelemetryClient(options, provider => () => provider.GetRequiredService<Switch>().On);

        var provider = services.BuildServiceProvider();
        var gate = provider.GetRequiredService<IOptions<PostHogOptions>>().Value.BeforeSend;

        gate.Should().NotBeNull();

        provider.Dispose();

        var sending = () => gate(Sample());

        sending.Should().NotThrow<ObjectDisposedException>();
        gate(Sample()).Should().BeNull();
    }

    [Fact]
    public void A_null_sink_accepts_an_undeclared_event_without_complaint()
    {
        var sink = new NullTelemetrySink();

        sink.Capture(new TelemetryEvent("whatever", new Dictionary<string, object?>()));

        sink.FlushAsync().IsCompletedSuccessfully.Should().BeTrue();
    }

    private static string Encoded(string token)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(token));

    private static CapturedEvent Sample()
        => new("app_started", "install", [], DateTimeOffset.UnixEpoch);

    private sealed class Switch
    {
        public bool On { get; set; } = true;
    }

    private static string SourceRoot([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src"));

    private sealed class EmptyProvider : IServiceProvider
    {
        public static readonly EmptyProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
