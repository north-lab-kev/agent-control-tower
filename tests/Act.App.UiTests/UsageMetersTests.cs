using System.Globalization;
using Act.App.Usage;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.App.UiTests;

// The only place ACT states a number it did not compute itself, so the three things worth being sure
// of are what a rolled-over window reports, which pressure tint a reading earns, and that a disabled
// agent says nothing at all.
public class UsageMetersTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly AgentType[] Both = [AgentType.ClaudeCode, AgentType.Codex];

    public UsageMetersTests() => CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = new CultureInfo("en");

    [Fact]
    public void A_reading_becomes_one_meter_per_window()
    {
        var meters = UsageMeters.For([Available(AgentType.ClaudeCode, (UsageWindowKind.Session, 40), (UsageWindowKind.Weekly, 12))], Both, Now);

        meters.Should().HaveCount(2);
        meters[0].Label.Should().Be("Claude 5h");
        meters[1].Label.Should().Be("Claude week");
    }

    // A window is named by the length the server declared, not by a fixed caption pair: a free Codex
    // plan reports a single 30-day window and hardcoding two bars would mislabel it.
    [Fact]
    public void A_monthly_window_is_named_as_one()
    {
        var meters = UsageMeters.For([Available(AgentType.Codex, (UsageWindowKind.Monthly, 17))], Both, Now);

        meters.Should().ContainSingle().Which.Label.Should().Be("Codex month");
    }

    [Fact]
    public void The_windows_read_shortest_first()
    {
        var meters = UsageMeters.For(
            [Available(AgentType.Codex, (UsageWindowKind.Weekly, 10), (UsageWindowKind.Session, 20))],
            Both,
            Now);

        meters.Select(meter => meter.Label).Should().Equal("Codex 5h", "Codex week");
    }

    // A disabled agent cannot be given a task, so its quota is not a number you can act on.
    [Fact]
    public void An_agent_you_have_switched_off_shows_nothing()
    {
        var readings = new[] { Available(AgentType.ClaudeCode, (UsageWindowKind.Session, 40)) };

        UsageMeters.For(readings, [AgentType.Codex], Now).Should().BeEmpty();
        UsageMeters.For(readings, [], Now).Should().BeEmpty();
    }

    [Fact]
    public void An_unavailable_agent_produces_a_notice_rather_than_a_meter()
    {
        var readings = new[] { UsageProbeResult.Unavailable(AgentType.ClaudeCode, UsageAvailability.Expired, Now) };

        UsageMeters.For(readings, Both, Now).Should().BeEmpty();

        var notice = UsageMeters.Notices(readings, Both).Should().ContainSingle().Subject;

        notice.Label.Should().Be("Claude");
        notice.Reason.Should().Be("token expired");
    }

    [Theory]
    [InlineData(UsageAvailability.NotSignedIn, "not signed in")]
    [InlineData(UsageAvailability.Expired, "token expired")]
    [InlineData(UsageAvailability.Unauthorized, "token refused")]
    [InlineData(UsageAvailability.Unreachable, "no connection")]
    [InlineData(UsageAvailability.Failed, "unreadable reply")]
    public void A_notice_says_which_kind_of_missing_it_is(UsageAvailability availability, string expected)
    {
        var notices = UsageMeters.Notices([UsageProbeResult.Unavailable(AgentType.Codex, availability, Now)], Both);

        notices.Should().ContainSingle().Which.Reason.Should().Be(expected);
    }

    // `Off` is a withdrawal, not an outcome to render: it is what `UsageState` removes on.
    [Fact]
    public void An_agent_that_is_off_is_neither_a_meter_nor_a_notice()
    {
        var readings = new[] { UsageProbeResult.Unavailable(AgentType.ClaudeCode, UsageAvailability.Off, Now) };

        UsageMeters.For(readings, Both, Now).Should().BeEmpty();
        UsageMeters.Notices(readings, Both).Should().BeEmpty();
    }

    // ACT polls, so a window held past its own reset says nothing about the new one — the stale
    // percentage describes a window that no longer exists.
    [Fact]
    public void A_window_held_past_its_reset_reports_zero()
    {
        var reading = UsageProbeResult.Of(new AgentUsage(
            AgentType.ClaudeCode,
            [new UsageWindow(UsageWindowKind.Session, 100, Now.AddHours(-1))],
            Now.AddHours(-4),
            LimitReached: true,
            Plan: null));

        var meter = UsageMeters.For([reading], Both, Now).Should().ContainSingle().Subject;

        meter.Percent.Should().Be(0);
        meter.Value.Should().Be("0%");
        meter.State.Should().Be("rolled");
        meter.Reset.Should().Be("· reset · no activity");
    }

    [Theory]
    [InlineData(10, "low")]
    [InlineData(74, "low")]
    [InlineData(75, "mid")]
    [InlineData(89, "mid")]
    [InlineData(90, "high")]
    [InlineData(100, "high")]
    public void The_tint_follows_the_pressure(int percent, string expected)
    {
        var meters = UsageMeters.For([Available(AgentType.Codex, (UsageWindowKind.Session, percent))], Both, Now);

        meters.Should().ContainSingle().Which.State.Should().Be(expected);
    }

    // The account-level flag says a limit was hit without saying which window, so any live window is
    // critical while it is set.
    [Fact]
    public void A_reported_limit_is_critical_whatever_the_percentage_says()
    {
        var reading = UsageProbeResult.Of(new AgentUsage(
            AgentType.Codex,
            [new UsageWindow(UsageWindowKind.Session, 5, Now.AddHours(2))],
            Now,
            LimitReached: true,
            Plan: null));

        UsageMeters.For([reading], Both, Now).Should().ContainSingle().Which.State.Should().Be("high");
    }

    // The question a quota answers is "how long have I got", so the span coarsens with distance.
    [Theory]
    [InlineData(45, "· in 45m")]
    [InlineData(150, "· in 2h 30m")]
    [InlineData(3000, "· in 2d 2h")]
    public void The_reset_is_worded_at_the_scale_it_is_read_at(int minutesAway, string expected)
    {
        var meters = UsageMeters.For(
            [Available(AgentType.ClaudeCode, (UsageWindowKind.Session, 20), minutesAway)],
            Both,
            Now);

        meters.Should().ContainSingle().Which.Reset.Should().Be(expected);
    }

    // Never "0 minutes": a window with thirty seconds left has not reset, and rounding it away would
    // read as though it had. The reset wording and `rolled` are the two sides of that boundary.
    [Fact]
    public void A_window_seconds_from_resetting_still_has_a_minute()
    {
        var reading = UsageProbeResult.Of(new AgentUsage(
            AgentType.ClaudeCode,
            [new UsageWindow(UsageWindowKind.Session, 99, Now.AddSeconds(30))],
            Now,
            LimitReached: false,
            Plan: null));

        var meter = UsageMeters.For([reading], Both, Now).Should().ContainSingle().Subject;

        meter.Reset.Should().Be("· in 1m");
        meter.Percent.Should().Be(99, "it has not rolled over yet");
    }

    private static UsageProbeResult Available(
        AgentType agent,
        params (UsageWindowKind Kind, int Percent)[] windows)
        => Available(agent, windows[0], 120, windows[1..]);

    private static UsageProbeResult Available(
        AgentType agent,
        (UsageWindowKind Kind, int Percent) window,
        int minutesAway)
        => Available(agent, window, minutesAway, []);

    private static UsageProbeResult Available(
        AgentType agent,
        (UsageWindowKind Kind, int Percent) first,
        int minutesAway,
        (UsageWindowKind Kind, int Percent)[] rest)
        => UsageProbeResult.Of(new AgentUsage(
            agent,
            [
                new UsageWindow(first.Kind, first.Percent, Now.AddMinutes(minutesAway)),
                .. rest.Select(w => new UsageWindow(w.Kind, w.Percent, Now.AddMinutes(minutesAway))),
            ],
            Now,
            LimitReached: false,
            Plan: null));
}
