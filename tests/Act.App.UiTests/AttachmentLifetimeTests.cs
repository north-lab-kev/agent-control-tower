using Act.App.Cards;
using Act.Core.Model;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// Files are written the moment they are attached, so the interesting behaviour is not the write —
// it is who is allowed to delete them, and when. Three answers: a purge (the only place a card
// leaves the store), a duplicate (the copy owns its own), and the startup sweep (folders no card
// claims). A soft delete deletes nothing, because the archive can put the card back.
public class AttachmentLifetimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Purging_the_archive_takes_the_files_with_it()
    {
        var card = Card();
        card.DeletedAt = Now.AddDays(-1);

        var (board, attachments) = await BoardOf(card);

        await board.PurgeArchivedAsync();

        attachments.Cleared.Should().Equal([card.Id]);
    }

    // The whole reason the delete above is soft: a restored card whose attachments were deleted
    // would be a card naming files that are gone.
    [Fact]
    public async Task Archiving_a_card_keeps_its_files()
    {
        var card = Card();
        var (board, attachments) = await BoardOf(card);

        await board.DeleteAsync(card, includeChildren: false);

        attachments.Cleared.Should().BeEmpty();
    }

    [Fact]
    public async Task A_duplicate_gets_its_own_copy_of_the_files()
    {
        var card = Card();
        card.Attachments = [new TaskAttachment { FileName = "spec.md" }];

        var (board, attachments) = await BoardOf(card);

        var copy = await board.DuplicateAsync(card);

        copy.Attachments.Should().ContainSingle().Which.FileName.Should().Be("spec.md");
        attachments.Copied.Should().Equal([(card.Id, copy.Id)]);
    }

    [Fact]
    public async Task The_sweep_clears_a_folder_no_card_claims()
    {
        var card = Card();
        var (board, attachments) = await BoardOf(card);
        var abandoned = Guid.Parse("11111111-2222-3333-4444-555555555555");

        await attachments.SaveAsync(card.Id, "kept.md", new MemoryStream());
        await attachments.SaveAsync(abandoned, "orphan.md", new MemoryStream());

        new AttachmentSweep(board, attachments, NullLogger<AttachmentSweep>.Instance).Run();

        attachments.Cleared.Should().Equal([abandoned]);
    }

    // The guard that matters most, because the sweep deletes without being asked: an empty board is
    // indistinguishable from a board that never arrived — a store another instance holds, a load that
    // threw upstream — and acting on that assumption costs the user their files. A store with no cards
    // has nothing worth reclaiming anyway, so refusing is free.
    [Fact]
    public async Task Nothing_is_swept_when_no_cards_are_loaded()
    {
        var attachments = new FakeAttachmentStore();
        var board = new BoardState(new FakeCardStore([]), attachments, new FrozenClock(Now));

        await board.LoadAsync();

        await attachments.SaveAsync(Guid.NewGuid(), "someone-elses.png", new MemoryStream());

        new AttachmentSweep(board, attachments, NullLogger<AttachmentSweep>.Instance).Run();

        attachments.Cleared.Should().BeEmpty();
    }

    // Including one that is only in the archive: an archived card is restorable, so its folder is
    // claimed exactly as a live card's is.
    [Fact]
    public async Task The_sweep_leaves_an_archived_cards_folder_alone()
    {
        var card = Card();
        card.DeletedAt = Now.AddDays(-1);

        var (board, attachments) = await BoardOf(card);

        await attachments.SaveAsync(card.Id, "kept.md", new MemoryStream());

        new AttachmentSweep(board, attachments, NullLogger<AttachmentSweep>.Instance).Run();

        attachments.Cleared.Should().BeEmpty();
    }

    private static async Task<(BoardState, FakeAttachmentStore)> BoardOf(params Card[] cards)
    {
        var attachments = new FakeAttachmentStore();
        var board = new BoardState(new FakeCardStore(cards), attachments, new FrozenClock(Now));

        await board.LoadAsync();

        return (board, attachments);
    }

    private static Card Card() => new()
    {
        Number = 1000,
        Title = "A task",
        Column = BoardColumn.Ready,
        WorkingDir = "/dev/act",
    };
}
