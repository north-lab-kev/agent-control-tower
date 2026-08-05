using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.App.UiTests;

public class BoardRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan TenDays = TimeSpan.FromDays(10);

    [Fact]
    public async Task An_old_completed_card_leaves_the_board_for_the_archive()
    {
        var old = Completed(Now.AddDays(-30));
        var board = await BoardOf(old);

        await board.ApplyRetentionAsync(TenDays);

        board.In(BoardColumn.Completed).Should().BeEmpty();
        board.Archived.Should().ContainSingle().Which.Id.Should().Be(old.Id);
        old.ArchivedAt.Should().Be(Now);
        old.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task A_recent_completed_card_stays_on_the_board()
    {
        var recent = Completed(Now.AddDays(-2));
        var board = await BoardOf(recent);

        await board.ApplyRetentionAsync(TenDays);

        board.In(BoardColumn.Completed).Should().ContainSingle().Which.Id.Should().Be(recent.Id);
        board.HasArchived.Should().BeFalse();
    }

    [Fact]
    public async Task No_window_archives_nothing()
    {
        var board = await BoardOf(Completed(Now.AddYears(-1)));

        await board.ApplyRetentionAsync(null);

        board.In(BoardColumn.Completed).Should().ContainSingle();
        board.HasArchived.Should().BeFalse();
    }

    [Fact]
    public async Task A_restored_card_is_back_on_the_board_and_stays_there()
    {
        var card = Completed(Now.AddDays(-30));
        var board = await BoardOf(card);

        await board.ApplyRetentionAsync(TenDays);
        await board.RestoreAsync(card);
        await board.ApplyRetentionAsync(TenDays);

        board.In(BoardColumn.Completed).Should().ContainSingle().Which.Id.Should().Be(card.Id);
        card.ArchivedAt.Should().BeNull();
        card.KeepOnBoard.Should().BeTrue();
    }

    [Fact]
    public async Task Clearing_the_archive_takes_the_auto_archived_cards_too()
    {
        var board = await BoardOf(Completed(Now.AddDays(-30)));

        await board.ApplyRetentionAsync(TenDays);
        await board.PurgeArchivedAsync();

        board.All.Should().BeEmpty();
    }

    private static async Task<BoardState> BoardOf(params Card[] cards)
    {
        var board = new BoardState(new FakeCardStore(cards), new FakeAttachmentStore(), new FrozenClock(Now));

        await board.LoadAsync();

        return board;
    }

    private static Card Completed(DateTimeOffset completedAt) => new()
    {
        Title = "Signed off",
        Column = BoardColumn.Completed,
        CompletedAt = completedAt,
    };

}
