using Act.Core.Model;
using Act.Core.Scheduling;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class ConcurrencySlotsTests
{
    [Fact]
    public void An_executing_card_occupies_a_slot()
        => ConcurrencySlots.Occupies(Card(BoardColumn.Executing, Badge.Running)).Should().BeTrue();

    // A blocked session is a live agent process parked at a prompt, which costs exactly what a
    // working one costs. A failed one has no process — and still counts, because it is a failure
    // the user has not dealt with and one Retry from being live again; a queue that launched past
    // a growing pile of them would turn one broken task into twenty.
    [Theory]
    [InlineData(Badge.NeedsPermission)]
    [InlineData(Badge.NeedsAnswer)]
    [InlineData(Badge.Compacting)]
    [InlineData(Badge.Error)]
    [InlineData(Badge.Killed)]
    public void Every_card_in_your_turn_but_one_occupies_a_slot(Badge badge)
        => ConcurrencySlots.Occupies(Card(BoardColumn.YourTurn, badge)).Should().BeTrue();

    // And the one that does not: the turn is over, the session is finished, and all that is left
    // is a signature.
    [Fact]
    public void A_card_awaiting_sign_off_occupies_nothing()
        => ConcurrencySlots.Occupies(Card(BoardColumn.YourTurn, Badge.ReadyForReview)).Should().BeFalse();

    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Completed)]
    public void Nothing_before_or_after_the_run_occupies_one(BoardColumn column)
        => ConcurrencySlots.Occupies(Card(column, null)).Should().BeFalse();

    [Fact]
    public void An_archived_card_occupies_nothing()
    {
        var card = Card(BoardColumn.Executing, Badge.Running);
        card.ArchivedAt = DateTimeOffset.UtcNow;

        ConcurrencySlots.Occupies(card).Should().BeFalse();
    }

    [Fact]
    public void The_cap_counts_only_the_occupied_ones()
    {
        Card[] cards =
        [
            Card(BoardColumn.Executing, Badge.Running),
            Card(BoardColumn.YourTurn, Badge.NeedsPermission),
            Card(BoardColumn.YourTurn, Badge.ReadyForReview),
            Card(BoardColumn.Ready, null),
        ];

        ConcurrencySlots.Used(cards).Should().Be(2);
        ConcurrencySlots.HasRoom(cards, 2).Should().BeFalse();
        ConcurrencySlots.HasRoom(cards, 3).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, ConcurrencySlots.Minimum)]
    [InlineData(-4, ConcurrencySlots.Minimum)]
    [InlineData(500, ConcurrencySlots.Maximum)]
    [InlineData(5, 5)]
    public void The_cap_is_clamped(int given, int expected)
        => ConcurrencySlots.Clamp(given).Should().Be(expected);

    private static Card Card(BoardColumn column, Badge? badge) => new()
    {
        Title = "A task",
        Column = column,
        Badge = badge,
    };
}
