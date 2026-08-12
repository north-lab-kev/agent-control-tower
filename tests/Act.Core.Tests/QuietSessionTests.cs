using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class QuietSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_running_card_that_has_gone_quiet_reports_how_long()
        => QuietSession.QuietFor(Executing(Now.AddMinutes(-22)), Now)
            .Should().Be(TimeSpan.FromMinutes(22));

    [Fact]
    public void A_card_inside_the_threshold_reports_nothing()
        => QuietSession.QuietFor(Executing(Now.AddMinutes(-2)), Now).Should().BeNull();

    [Fact]
    public void The_threshold_itself_counts_as_quiet()
        => QuietSession.QuietFor(Executing(Now - QuietSession.Threshold), Now)
            .Should().Be(QuietSession.Threshold);

    // A card in Your turn is quiet *because* it is waiting for the user — that is its resting state,
    // and saying so would put a "look at me" on every card already asking to be looked at.
    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.YourTurn)]
    [InlineData(BoardColumn.Completed)]
    public void Only_a_card_in_executing_can_be_quiet(BoardColumn column)
    {
        var card = Executing(Now.AddHours(-9));

        card.Column = column;

        QuietSession.QuietFor(card, Now).Should().BeNull();
    }

    // Before the first event there is nothing to measure from, and a launch that has not been heard
    // from yet is the startup prompt's business, not this one's.
    [Fact]
    public void A_card_that_has_never_reported_activity_reports_nothing()
        => QuietSession.QuietFor(
                new Card { Column = BoardColumn.Executing, Metrics = new CardMetrics() },
                Now)
            .Should().BeNull();

    [Fact]
    public void A_card_with_no_metrics_at_all_reports_nothing()
        => QuietSession.QuietFor(new Card { Column = BoardColumn.Executing }, Now).Should().BeNull();

    private static Card Executing(DateTimeOffset lastActivity) => new()
    {
        Column = BoardColumn.Executing,
        Badge = Badge.Running,
        Metrics = new CardMetrics { LastActivityAt = lastActivity },
    };
}
