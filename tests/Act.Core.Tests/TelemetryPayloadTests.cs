using Act.Core.Model;
using Act.Core.Telemetry;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class TelemetryPayloadTests
{
    [Fact]
    public void An_undeclared_event_never_leaves()
    {
        TelemetryPayload.Sanitize(Event("prompt_text", [])).Should().BeNull();
    }

    [Fact]
    public void An_undeclared_property_is_dropped()
    {
        var sanitized = TelemetryPayload.Sanitize(Event(
            TelemetryEvents.Names.TaskLaunched,
            [
                new(TelemetryProperties.Agent, AgentType.ClaudeCode),
                new("initial_prompt", "Rename the widget"),
            ]));

        sanitized!.Properties.Should().ContainKey(TelemetryProperties.Agent);
        sanitized.Properties.Should().NotContainKey("initial_prompt");
    }

    [Theory]
    [InlineData(@"C:\Users\someone\repo")]
    [InlineData("/home/someone/repo")]
    [InlineData("~/repo")]
    [InlineData("say \"hello\"")]
    [InlineData("two\nlines")]
    public void A_value_that_looks_like_a_path_or_free_text_is_dropped(string leaked)
    {
        var sanitized = TelemetryPayload.Sanitize(Event(
            TelemetryEvents.Names.AppStarted,
            [new(TelemetryProperties.OperatingSystem, leaked)]));

        sanitized!.Properties.Should().BeEmpty();
    }

    [Fact]
    public void A_version_string_and_an_os_description_survive()
    {
        var sanitized = TelemetryPayload.Sanitize(Event(
            TelemetryEvents.Names.AppStarted,
            [
                new(TelemetryProperties.AppVersion, "0.4.1-beta"),
                new(TelemetryProperties.OperatingSystem, "Microsoft Windows NT 10.0.26200.0"),
                new(TelemetryProperties.Locale, "fr-CA"),
            ]));

        sanitized!.Properties.Should().HaveCount(3);
    }

    [Fact]
    public void A_symbol_longer_than_the_cap_is_dropped()
    {
        var sanitized = TelemetryPayload.Sanitize(Event(
            TelemetryEvents.Names.AppStarted,
            [new(TelemetryProperties.AppVersion, new string('v', TelemetryPayload.LongestSymbol + 1))]));

        sanitized!.Properties.Should().BeEmpty();
    }

    [Fact]
    public void An_enum_becomes_its_name()
    {
        var sanitized = TelemetryPayload.Sanitize(Event(
            TelemetryEvents.Names.TaskLaunched,
            [new(TelemetryProperties.Agent, AgentType.ClaudeCode)]));

        sanitized!.Properties[TelemetryProperties.Agent].Should().Be("ClaudeCode");
    }

    [Fact]
    public void Scalars_survive_and_a_null_is_dropped()
    {
        var sanitized = TelemetryPayload.Sanitize(Event(
            TelemetryEvents.Names.TaskLaunched,
            [
                new(TelemetryProperties.AttachmentCount, 4),
                new(TelemetryProperties.DependencyCount, 91_204L),
                new(TelemetryProperties.Scheduled, true),
                new(TelemetryProperties.Origin, null),
            ]));

        sanitized!.Properties[TelemetryProperties.AttachmentCount].Should().Be(4);
        sanitized.Properties[TelemetryProperties.DependencyCount].Should().Be(91_204L);
        sanitized.Properties[TelemetryProperties.Scheduled].Should().Be(true);
        sanitized.Properties.Should().NotContainKey(TelemetryProperties.Origin);
    }

    [Fact]
    public void An_object_nobody_declared_a_shape_for_is_dropped()
    {
        var sanitized = TelemetryPayload.Sanitize(Event(
            TelemetryEvents.Names.TaskLaunched,
            [new(TelemetryProperties.Agent, new Card { Title = "Rename the widget" })]));

        sanitized!.Properties.Should().BeEmpty();
    }

    [Fact]
    public void A_list_keeps_only_its_symbols()
    {
        var sanitized = TelemetryPayload.Sanitize(Event(
            TelemetryEvents.Names.AppError,
            [
                new(TelemetryProperties.Frames, new[] { "Act.App.Sessions.SessionLauncher.BeginAsync", "/src/x.cs" }),
            ]));

        sanitized!.Properties[TelemetryProperties.Frames].Should()
            .BeEquivalentTo(new[] { "Act.App.Sessions.SessionLauncher.BeginAsync" });
    }

    [Fact]
    public void A_list_with_nothing_worth_keeping_is_dropped()
    {
        var sanitized = TelemetryPayload.Sanitize(Event(
            TelemetryEvents.Names.AppError,
            [new(TelemetryProperties.Frames, new[] { @"C:\one", @"C:\two" })]));

        sanitized!.Properties.Should().BeEmpty();
    }

    [Fact]
    public void A_list_is_capped()
    {
        var frames = Enumerable.Range(0, TelemetryFault.MostFrames + 5).Select(index => $"Act.Frame{index}");

        var sanitized = TelemetryPayload.Sanitize(Event(
            TelemetryEvents.Names.AppError,
            [new(TelemetryProperties.Frames, frames.ToArray())]));

        sanitized!.Properties[TelemetryProperties.Frames].Should()
            .BeOfType<string[]>().Which.Should().HaveCount(TelemetryFault.MostFrames);
    }

    private static TelemetryEvent Event(string name, KeyValuePair<string, object?>[] properties)
        => new(name, new Dictionary<string, object?>(properties, StringComparer.Ordinal));
}
