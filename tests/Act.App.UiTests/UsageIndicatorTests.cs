using Act.App.Components.Layout;
using Act.Core.Model;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// `UsageMeters` decides what the strip says and `UsageMetersTests` owns that. What is left is the part
// only a component has: the strip is absent entirely when there is nothing to report, a meter and a
// notice are different shapes in the DOM, and the two subscriptions keep it current — including the
// settings one, which exists so a meter leaves the bar the moment an agent is switched off rather than
// whenever the next poll lands.
public class UsageIndicatorTests : ComponentTest
{
    [Fact]
    public void Nothing_to_report_means_no_strip_at_all()
    {
        Render<UsageIndicator>().FindAll("div.usage").Should().BeEmpty();
    }

    [Fact]
    public void A_reading_becomes_a_meter()
    {
        Usage.Publish(Reading(AgentType.ClaudeCode, 40));

        var cut = Render<UsageIndicator>();

        var meter = cut.Find("div.meter");

        meter.QuerySelector(".mvalue")!.TextContent.Should().Contain("40");
        meter.ClassList.Should().NotContain("warn");
        cut.FindAll("div.meter .mbar").Should().ContainSingle();
    }

    // A notice is not a meter with no number: it carries the reason instead of a bar, which is what
    // stops the bar drawing 0% for a quota nobody could read.
    [Fact]
    public void An_unavailable_agent_becomes_a_notice_rather_than_an_empty_meter()
    {
        Usage.Publish(UsageProbeResult.Unavailable(AgentType.ClaudeCode, UsageAvailability.NotSignedIn, Now));

        var cut = Render<UsageIndicator>();

        cut.Find("div.meter").ClassList.Should().Contain("warn");
        cut.FindAll("div.meter .nrule").Should().ContainSingle();
        cut.FindAll("div.meter .mbar").Should().BeEmpty();
    }

    [Fact]
    public void A_meter_per_enabled_agent()
    {
        Usage.Publish(Reading(AgentType.ClaudeCode, 40));
        Usage.Publish(Reading(AgentType.Codex, 70));

        Render<UsageIndicator>().FindAll("div.meter").Should().HaveCount(2);
    }

    // The whole reason `UsageState.Changed` is subscribed: a poll lands on a background loop, and the
    // bar has to follow without anything re-rendering the layout above it.
    [Fact]
    public void A_later_reading_reaches_a_strip_that_is_already_on_screen()
    {
        var cut = Render<UsageIndicator>();

        cut.FindAll("div.meter").Should().BeEmpty();

        Usage.Publish(Reading(AgentType.ClaudeCode, 40));

        cut.WaitForElement("div.meter").QuerySelector(".mvalue")!.TextContent.Should().Contain("40");
    }

    // The second subscription, and the one that is easy to leave out: switching an agent off has to
    // take its meter with it immediately, because the reading it was drawn from is still in `UsageState`
    // until the next poll withdraws it.
    [Fact]
    public void Switching_an_agent_off_takes_its_meter_with_it()
    {
        Usage.Publish(Reading(AgentType.ClaudeCode, 40));
        Usage.Publish(Reading(AgentType.Codex, 70));

        var cut = Render<UsageIndicator>();

        cut.FindAll("div.meter").Should().HaveCount(2);

        Settings.SetDefaults(new AgentDefaults { Agent = AgentType.Codex, Enabled = false });

        cut.WaitForAssertion(() => cut.FindAll("div.meter").Should().ContainSingle());
    }

    private static UsageProbeResult Reading(AgentType agent, int percent)
        => UsageProbeResult.Of(new AgentUsage(
            agent,
            [new UsageWindow(UsageWindowKind.Session, percent, Now.AddHours(3))],
            Now,
            LimitReached: false,
            Plan: null));
}
