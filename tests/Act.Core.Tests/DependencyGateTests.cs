using Act.Core.Model;
using Act.Core.Scheduling;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class DependencyGateTests
{
    [Fact]
    public void A_card_with_no_dependencies_waits_for_nothing()
        => DependencyGate.Blocking(Dependent(), []).Should().BeNull();

    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    public void An_unfinished_prerequisite_blocks(BoardColumn column)
    {
        var prerequisite = Prerequisite(column);

        DependencyGate.Blocking(Dependent(prerequisite), [prerequisite]).Should().Be(prerequisite);
    }

    // Completed, not "finished a turn" — the same line the folder guard reads, for the same reason:
    // the work is not done until a human says so.
    [Fact]
    public void A_completed_prerequisite_does_not()
    {
        var prerequisite = Prerequisite(BoardColumn.Completed);

        DependencyGate.Blocking(Dependent(prerequisite), [prerequisite]).Should().BeNull();
    }

    // The alternative is a card that can never launch and says nothing about why.
    [Fact]
    public void A_prerequisite_the_store_no_longer_has_counts_as_satisfied()
    {
        var card = Dependent();
        card.DependsOn.Add(Guid.NewGuid());

        DependencyGate.Blocking(card, []).Should().BeNull();
    }

    [Fact]
    public void A_deleted_prerequisite_counts_as_satisfied_too()
    {
        var prerequisite = Prerequisite(BoardColumn.Ready);
        prerequisite.DeletedAt = DateTimeOffset.UtcNow;

        DependencyGate.Blocking(Dependent(prerequisite), [prerequisite]).Should().BeNull();
    }

    [Fact]
    public void The_first_unfinished_prerequisite_is_the_one_reported()
    {
        var done = Prerequisite(BoardColumn.Completed);
        var pending = Prerequisite(BoardColumn.Executing);

        var card = Dependent(done, pending);

        DependencyGate.Blocking(card, [done, pending]).Should().Be(pending);
    }

    private static Card Dependent(params Card[] prerequisites)
    {
        var card = new Card { Title = "The dependent one", Column = BoardColumn.Ready };

        foreach (var prerequisite in prerequisites)
            card.DependsOn.Add(prerequisite.Id);

        return card;
    }

    private static Card Prerequisite(BoardColumn column) => new()
    {
        Number = 1039,
        Title = "First this",
        Column = column,
    };
}
