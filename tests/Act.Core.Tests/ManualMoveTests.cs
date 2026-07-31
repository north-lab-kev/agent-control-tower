using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class ManualMoveTests
{
    private static readonly BoardColumn[] AllColumns = Enum.GetValues<BoardColumn>();

    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    public void A_human_controlled_card_can_be_picked_up(BoardColumn column)
        => ManualMove.CanDrag(column).Should().BeTrue();

    // It lifts so it can be dropped on Completed — the sign-off — and nowhere else: the drop side
    // of that transition belongs to `CardCompletion`, not to `IsAllowed`.
    [Fact]
    public void A_card_handed_back_to_the_user_lifts_for_the_sign_off()
        => ManualMove.CanDrag(BoardColumn.YourTurn).Should().BeTrue();

    [Theory]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.Completed)]
    public void A_card_the_user_no_longer_moves_does_not_lift(BoardColumn column)
        => ManualMove.CanDrag(column).Should().BeFalse();

    [Fact]
    public void Preparing_and_ready_are_interchangeable_by_hand()
    {
        ManualMove.IsAllowed(BoardColumn.Preparing, BoardColumn.Ready).Should().BeTrue();
        ManualMove.IsAllowed(BoardColumn.Ready, BoardColumn.Preparing).Should().BeTrue();
    }

    [Fact]
    public void Launching_is_not_a_manual_move()
        => ManualMove.IsAllowed(BoardColumn.Ready, BoardColumn.Executing).Should().BeFalse();

    [Fact]
    public void Reopening_is_not_a_manual_move_yet()
        => ManualMove.IsAllowed(BoardColumn.Completed, BoardColumn.YourTurn).Should().BeFalse();

    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    [InlineData(BoardColumn.Completed)]
    public void A_column_never_accepts_a_drop_from_itself(BoardColumn column)
        => ManualMove.IsAllowed(column, column).Should().BeFalse();

    [Fact]
    public void Only_the_two_human_transitions_are_allowed_across_every_pair()
    {
        var allowed =
            from source in AllColumns
            from target in AllColumns
            where ManualMove.IsAllowed(source, target)
            select (source, target);

        allowed.Should().BeEquivalentTo(
        [
            (BoardColumn.Preparing, BoardColumn.Ready),
            (BoardColumn.Ready, BoardColumn.Preparing),
        ]);
    }

    [Fact]
    public void Nothing_can_be_dropped_onto_a_machine_column()
    {
        var machineColumns = new[]
        {
            BoardColumn.Executing, BoardColumn.YourTurn, BoardColumn.Completed,
        };

        foreach (var target in machineColumns)
            foreach (var source in AllColumns)
                ManualMove.IsAllowed(source, target).Should().BeFalse(
                    $"{source} → {target} crosses the launch boundary");
    }
}
