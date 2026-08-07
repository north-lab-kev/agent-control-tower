using Act.Core.Telemetry;
using Act.Infrastructure.Telemetry;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PostHog;
using PostHog.Features;

namespace Act.Infrastructure.Tests;

// The transport half of the vendor folder. What it owes the rest of ACT is two things: an undeclared
// payload never reaches the wire, and nothing here ever reaches a caller — every call site is a
// launch, a sign-off or a startup step.
public class PostHogTelemetrySinkTests
{
    private const string InstallId = "install-1";

    private readonly IPostHogClient client = Substitute.For<IPostHogClient>();

    private PostHogTelemetrySink Sink()
        => new(client, InstallId, NullLogger<PostHogTelemetrySink>.Instance);

    private IReadOnlyList<object?[]> Captures() =>
    [
        .. client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IPostHogClient.Capture))
            .Select(call => call.GetArguments()),
    ];

    [Fact]
    public void A_declared_event_is_captured_against_the_install_id()
    {
        Sink().Capture(new TelemetryEvent(
            TelemetryEvents.Names.AppError,
            new Dictionary<string, object?> { [TelemetryProperties.Fatal] = true }));

        var captured = Captures().Should().ContainSingle().Subject;

        captured.Should().Contain(InstallId);
        captured.Should().Contain(TelemetryEvents.Names.AppError);
    }

    // The guard on `Probe` below. `WhenForAnyArgs` silently matches nothing if it names the wrong
    // overload, and a "does not throw" test then passes because nothing ever threw — which is how the
    // first version of `A_client_that_throws_does_not_disturb_the_caller` passed while covering none
    // of the catch it exists for.
    [Fact]
    public void The_extension_still_forwards_to_the_overload_these_tests_intercept()
    {
        Sink().Capture(new TelemetryEvent(
            TelemetryEvents.Names.AppError,
            new Dictionary<string, object?> { [TelemetryProperties.Fatal] = true }));

        var intercepted = client.ReceivedCalls().Single().GetMethodInfo();

        intercepted.Name.Should().Be(nameof(IPostHogClient.Capture));
        intercepted.GetParameters()[4].ParameterType.Should().Be<FeatureFlagEvaluations?>();
    }

    [Fact]
    public void The_declared_properties_are_what_the_client_is_handed()
    {
        Sink().Capture(new TelemetryEvent(
            TelemetryEvents.Names.AppError,
            new Dictionary<string, object?>
            {
                [TelemetryProperties.Exception] = "InvalidOperationException",
                [TelemetryProperties.Fatal] = false,
            }));

        Properties().Should().Contain(new KeyValuePair<string, object>(
            TelemetryProperties.Exception,
            "InvalidOperationException"));
        Properties().Should().Contain(new KeyValuePair<string, object>(TelemetryProperties.Fatal, false));
    }

    // The sink is the last thing between a call site and the wire, so the two things `Sanitize`
    // refuses have to be refused here as well — not merely somewhere upstream.
    [Fact]
    public void An_undeclared_event_never_reaches_the_client()
    {
        Sink().Capture(new TelemetryEvent("task_completed", new Dictionary<string, object?>()));

        Captures().Should().BeEmpty();
    }

    [Fact]
    public void An_undeclared_property_is_dropped_on_the_way_through()
    {
        Sink().Capture(new TelemetryEvent(
            TelemetryEvents.Names.AppError,
            new Dictionary<string, object?>
            {
                [TelemetryProperties.Fatal] = true,
                ["prompt"] = "write me a parser",
            }));

        Properties().Should().ContainKey(TelemetryProperties.Fatal);
        Properties().Should().NotContainKey("prompt");
    }

    [Fact]
    public void A_value_that_is_not_a_bounded_scalar_is_dropped_and_the_event_still_goes()
    {
        Sink().Capture(new TelemetryEvent(
            TelemetryEvents.Names.AppError,
            new Dictionary<string, object?>
            {
                [TelemetryProperties.Fatal] = true,
                [TelemetryProperties.Exception] = @"C:\dev\act\Program.cs",
            }));

        Properties().Should().ContainKey(TelemetryProperties.Fatal);
        Properties().Should().NotContainKey(TelemetryProperties.Exception);
    }

    // A full queue or a disposed client. Neither is the caller's problem: nothing the user is doing
    // has failed, so it is logged and dropped.
    [Fact]
    public void A_client_that_throws_does_not_disturb_the_caller()
    {
        client.WhenForAnyArgs(Probe).Throw(new ObjectDisposedException("client"));

        var capture = () => Sink().Capture(new TelemetryEvent(
            TelemetryEvents.Names.AppError,
            new Dictionary<string, object?> { [TelemetryProperties.Fatal] = true }));

        capture.Should().NotThrow();
    }

    [Fact]
    public async Task A_flush_forwards_to_the_client()
    {
        await Sink().FlushAsync();

        await client.Received(1).FlushAsync();
    }

    // The last flush of a run happens during shutdown, where the provider may already be gone.
    [Fact]
    public async Task A_flush_that_throws_does_not_disturb_the_caller()
    {
        client.FlushAsync().Throws(new ObjectDisposedException("client"));

        var flush = async () => await Sink().FlushAsync();

        await flush.Should().NotThrowAsync();
    }

    // The sink calls the `CaptureExtensions` overload, which is not interceptable; what NSubstitute
    // sees is whichever interface method that extension forwards to — and `IPostHogClient` declares
    // two that differ only in their fifth parameter. Naming it here rather than at the call site
    // keeps a package bump that moves the forwarding to one edit, and
    // `The_extension_still_forwards_to_the_overload_these_tests_intercept` is what fails when it does.
    private static void Probe(IPostHogClient client)
        => client.Capture(
            default!,
            default!,
            default(Dictionary<string, object>?),
            default,
            default(FeatureFlagEvaluations?),
            default);

    private Dictionary<string, object> Properties()
        => Captures()
            .Should().ContainSingle().Subject
            .OfType<Dictionary<string, object>>()
            .Should().ContainSingle().Subject;
}
