using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class CompletedRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan TenDays = TimeSpan.FromDays(10);

    [Fact]
    public void A_completed_card_older_than_the_window_is_due()
        => CompletedRetention.IsDue(Completed(Now.AddDays(-11)), TenDays, Now).Should().BeTrue();

    [Fact]
    public void The_window_itself_counts_as_due()
        => CompletedRetention.IsDue(Completed(Now - TenDays), TenDays, Now).Should().BeTrue();

    [Fact]
    public void A_card_inside_the_window_stays()
        => CompletedRetention.IsDue(Completed(Now.AddDays(-9)), TenDays, Now).Should().BeFalse();

    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    public void Only_a_completed_card_is_ever_due(BoardColumn column)
    {
        var card = Completed(Now.AddYears(-1));

        card.Column = column;

        CompletedRetention.IsDue(card, TenDays, Now).Should().BeFalse();
    }

    [Fact]
    public void A_completed_card_with_no_completion_date_stays()
        => CompletedRetention.IsDue(new Card { Column = BoardColumn.Completed }, TenDays, Now)
            .Should().BeFalse();

    [Fact]
    public void A_card_the_user_deleted_is_not_due()
    {
        var card = Completed(Now.AddYears(-1));

        card.DeletedAt = Now.AddDays(-2);

        CompletedRetention.IsDue(card, TenDays, Now).Should().BeFalse();
    }

    [Fact]
    public void A_card_already_archived_is_not_due_again()
    {
        var card = Completed(Now.AddYears(-1));

        card.ArchivedAt = Now.AddDays(-2);

        CompletedRetention.IsDue(card, TenDays, Now).Should().BeFalse();
    }

    [Fact]
    public void A_card_the_user_restored_is_never_due_again()
    {
        var card = Completed(Now.AddYears(-1));

        card.KeepOnBoard = true;

        CompletedRetention.IsDue(card, TenDays, Now).Should().BeFalse();
    }

    [Fact]
    public void Due_returns_only_the_cards_past_the_window()
    {
        Card[] cards =
        [
            Completed(Now.AddDays(-30)),
            Completed(Now.AddDays(-1)),
            Completed(Now.AddDays(-10)),
        ];

        CompletedRetention.Due(cards, TenDays, Now).Should().Equal(cards[0], cards[2]);
    }

    [Theory]
    [InlineData(0, CompletedRetention.MinimumDays)]
    [InlineData(-5, CompletedRetention.MinimumDays)]
    [InlineData(10, 10)]
    [InlineData(5000, CompletedRetention.MaximumDays)]
    public void The_window_is_clamped_to_something_a_policy_can_mean(int days, int expected)
        => CompletedRetention.ClampDays(days).Should().Be(expected);

    [Fact]
    public void The_window_is_the_clamped_day_count()
        => CompletedRetention.Window(10).Should().Be(TenDays);

    private static Card Completed(DateTimeOffset completedAt) => new()
    {
        Column = BoardColumn.Completed,
        CompletedAt = completedAt,
    };
}
