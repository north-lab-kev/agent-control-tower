using Act.App.Usage;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.App.UiTests;

// The top bar reads from here, and so does the queue's backpressure — so a reading that outlives
// the reason it was taken is not a stale pixel, it is a number two different things act on.
public class UsageStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_reading_is_kept_per_agent()
    {
        var state = new UsageState();

        state.Publish(Reading(AgentType.ClaudeCode, 40));
        state.Publish(Reading(AgentType.Codex, 70));

        state.Results.Should().HaveCount(2);
    }

    // What switching an agent off has to do. Nothing else empties this, so without it the bar
    // answers a re-enabled agent with the percentage it had an hour ago — for a window that has
    // since rolled over.
    [Fact]
    public void Off_withdraws_the_agent_reading()
    {
        var state = new UsageState();

        state.Publish(Reading(AgentType.ClaudeCode, 40));
        state.Publish(UsageProbeResult.Unavailable(AgentType.ClaudeCode, UsageAvailability.Off, Now));

        state.Results.Should().BeEmpty();
    }

    // `Off` is republished on every pass of the poll loop for as long as the agent stays off, and
    // every announcement wakes the queue runner — so only the one that actually took something away
    // is news.
    [Fact]
    public void Off_announces_once_and_then_says_nothing()
    {
        var state = new UsageState();
        var announcements = 0;

        state.Publish(Reading(AgentType.ClaudeCode, 40));

        state.Changed += () => announcements++;

        state.Publish(UsageProbeResult.Unavailable(AgentType.ClaudeCode, UsageAvailability.Off, Now));
        state.Publish(UsageProbeResult.Unavailable(AgentType.ClaudeCode, UsageAvailability.Off, Now));
        state.Publish(UsageProbeResult.Unavailable(AgentType.ClaudeCode, UsageAvailability.Off, Now));

        announcements.Should().Be(1);
    }

    // Unlike `Off`, a named unavailability is something the bar shows, so it is recorded rather
    // than withdrawn.
    [Fact]
    public void An_unavailable_agent_still_has_something_to_report()
    {
        var state = new UsageState();

        state.Publish(UsageProbeResult.Unavailable(AgentType.Codex, UsageAvailability.NotSignedIn, Now));

        state.Results.Should().ContainSingle()
            .Which.Availability.Should().Be(UsageAvailability.NotSignedIn);
    }

    private static UsageProbeResult Reading(AgentType agent, int percent)
        => UsageProbeResult.Of(new AgentUsage(
            agent,
            [new UsageWindow(UsageWindowKind.Session, percent, Now.AddHours(3))],
            Now,
            LimitReached: false,
            Plan: null));
}
