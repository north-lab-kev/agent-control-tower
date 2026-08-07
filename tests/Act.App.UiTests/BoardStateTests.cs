using Act.App.Cards;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.App.UiTests;

// Every card write in ACT funnels through here — the rules engine, the launcher, the pumps, the
// retention sweep and every page. Retention and restore are covered by `BoardRetentionTests`; this
// is the rest: what a column arrival stamps, what a reorder writes, what a delete takes with it,
// and that concurrent writers cannot tear the board apart.
public class BoardStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);

    // A card landing in a column lands at the top of it. Decided in one place because half a dozen
    // callers assign a column, and a stamp only most of them remembered would leave cards sitting
    // wherever their previous column had put them.
    [Fact]
    public async Task A_card_arriving_in_a_column_lands_ahead_of_everything_already_there()
    {
        var first = Card(BoardColumn.Ready, order: 1);
        var second = Card(BoardColumn.Ready, order: 2);
        var arriving = Card(BoardColumn.Preparing, order: 1);

        var board = await BoardOf(first, second, arriving);

        arriving.Column = BoardColumn.Ready;
        await board.UpdateAsync(arriving);

        arriving.Order.Should().Be(0);
        board.In(BoardColumn.Ready).First().Id.Should().Be(arriving.Id);
    }

    // An ordinary save is not an arrival, or editing a title would send the card to the back of its
    // own lane.
    [Fact]
    public async Task Saving_a_card_that_has_not_moved_leaves_its_place_alone()
    {
        var card = Card(BoardColumn.Ready, order: 1);
        var other = Card(BoardColumn.Ready, order: 2);

        var board = await BoardOf(card, other);

        card.Title = "Renamed";
        await board.UpdateAsync(card);

        card.Order.Should().Be(1);
        board.In(BoardColumn.Ready).First().Id.Should().Be(card.Id);
    }

    [Fact]
    public async Task A_new_card_is_stamped_and_visible()
    {
        var board = await BoardOf(Card(BoardColumn.Preparing, order: 1));

        var created = Card(BoardColumn.Preparing, order: 5);
        await board.CreateAsync(created);

        created.Order.Should().Be(0);
        board.In(BoardColumn.Preparing).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_column_reads_in_the_order_the_user_put_it_in()
    {
        var board = await BoardOf(
            Card(BoardColumn.Ready, order: 3, number: 1000),
            Card(BoardColumn.Ready, order: 1, number: 1001),
            Card(BoardColumn.Ready, order: 2, number: 1002));

        board.In(BoardColumn.Ready).Select(card => card.Order).Should().Equal(1, 2, 3);
    }

    // Cards stored before ordering existed are all at zero, and `Number` descending keeps those
    // reading newest first, like every other column.
    [Fact]
    public async Task Cards_with_no_order_fall_back_to_newest_created_first()
    {
        var board = await BoardOf(
            Card(BoardColumn.Ready, order: 0, number: 1002),
            Card(BoardColumn.Ready, order: 0, number: 1000),
            Card(BoardColumn.Ready, order: 0, number: 1001));

        board.In(BoardColumn.Ready).Select(card => card.Number).Should().Equal(1002, 1001, 1000);
    }

    [Fact]
    public async Task A_reorder_resequences_the_lane()
    {
        var first = Card(BoardColumn.Ready, order: 1, number: 1000);
        var second = Card(BoardColumn.Ready, order: 2, number: 1001);
        var third = Card(BoardColumn.Ready, order: 3, number: 1002);

        var board = await BoardOf(first, second, third);

        await board.ReorderAsync(third, first);

        board.In(BoardColumn.Ready).Select(card => card.Number).Should().Equal(1002, 1000, 1001);
    }

    [Fact]
    public async Task A_reorder_across_columns_is_refused()
    {
        var ready = Card(BoardColumn.Ready, order: 1);
        var preparing = Card(BoardColumn.Preparing, order: 1);

        var board = await BoardOf(ready, preparing);

        await board.ReorderAsync(ready, preparing);

        ready.Order.Should().Be(1);
        ready.Column.Should().Be(BoardColumn.Ready);
    }

    [Fact]
    public async Task A_card_dropped_on_itself_reorders_nothing()
    {
        var card = Card(BoardColumn.Ready, order: 1);
        var board = await BoardOf(card, Card(BoardColumn.Ready, order: 2));

        await board.ReorderAsync(card, card);

        card.Order.Should().Be(1);
    }

    [Fact]
    public async Task A_legal_hand_move_is_stamped_on_the_timeline()
    {
        var card = Card(BoardColumn.Preparing, order: 1);
        var board = await BoardOf(card);

        await board.MoveAsync(card, BoardColumn.Ready);

        card.Column.Should().Be(BoardColumn.Ready);
        card.Transitions.Should().ContainSingle()
            .Which.Reason.Should().Be(TransitionReason.MovedByHand);
    }

    // Past the launch boundary a card moves only through automated transitions, and the guard is in
    // the model rather than only in the markup.
    [Fact]
    public async Task An_illegal_hand_move_is_refused_outright()
    {
        var card = Card(BoardColumn.Executing, order: 1);
        var board = await BoardOf(card);

        var move = async () => await board.MoveAsync(card, BoardColumn.Completed);

        await move.Should().ThrowAsync<InvalidOperationException>();

        card.Column.Should().Be(BoardColumn.Executing);
        card.Transitions.Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_card_takes_it_off_the_board_without_losing_it()
    {
        var card = Card(BoardColumn.Ready, order: 1);
        var board = await BoardOf(card);

        await board.DeleteAsync(card, includeChildren: false);

        board.In(BoardColumn.Ready).Should().BeEmpty();
        board.Archived.Should().ContainSingle();
        card.DeletedAt.Should().Be(Now);

        // Still addressable: the archive links to it and a restore has to be able to find it.
        board.Card(card.Id).Should().BeSameAs(card);
    }

    [Fact]
    public async Task Deleting_with_follow_ups_takes_the_whole_subtree()
    {
        var parent = Card(BoardColumn.Ready, order: 1);
        var child = Card(BoardColumn.Ready, order: 2);
        var grandchild = Card(BoardColumn.Ready, order: 3);

        parent.Children.Add(child.Id);
        child.Children.Add(grandchild.Id);

        var board = await BoardOf(parent, child, grandchild);

        await board.DeleteAsync(parent, includeChildren: true);

        board.Archived.Should().HaveCount(3);
    }

    [Fact]
    public async Task Deleting_without_follow_ups_leaves_them_on_the_board()
    {
        var parent = Card(BoardColumn.Ready, order: 1);
        var child = Card(BoardColumn.Ready, order: 2);

        parent.Children.Add(child.Id);

        var board = await BoardOf(parent, child);

        await board.DeleteAsync(parent, includeChildren: false);

        board.Archived.Should().ContainSingle();
        board.In(BoardColumn.Ready).Should().ContainSingle().Which.Id.Should().Be(child.Id);
    }

    // Lineage is stored on both sides, so a corrupt pair could form a loop — and a delete is the
    // wrong moment to hang.
    [Fact]
    public async Task A_lineage_loop_does_not_hang_a_delete()
    {
        var one = Card(BoardColumn.Ready, order: 1);
        var two = Card(BoardColumn.Ready, order: 2);

        one.Children.Add(two.Id);
        two.Children.Add(one.Id);

        var board = await BoardOf(one, two);

        await board.DeleteAsync(one, includeChildren: true);

        board.Archived.Should().HaveCount(2);
    }

    [Fact]
    public async Task Children_are_resolved_through_the_board()
    {
        var parent = Card(BoardColumn.Ready, order: 1);
        var child = Card(BoardColumn.Ready, order: 2);

        parent.Children.Add(child.Id);
        parent.Children.Add(Guid.NewGuid());

        var board = await BoardOf(parent, child);

        // The id nothing answers to is dropped rather than surfacing as a null row.
        board.ChildrenOf(parent).Should().ContainSingle().Which.Id.Should().Be(child.Id);
    }

    // `children` and `parentId` are stored on both sides, so a purge that skipped the unlink would
    // leave live cards pointing at rows that no longer exist.
    [Fact]
    public async Task Purging_unlinks_what_it_removes_from_a_parent_that_stays()
    {
        var parent = Card(BoardColumn.Ready, order: 1);
        var child = Card(BoardColumn.Completed, order: 1);

        parent.Children.Add(child.Id);
        child.ParentId = parent.Id;
        child.DeletedAt = Now.AddDays(-1);

        var board = await BoardOf(parent, child);

        await board.PurgeArchivedAsync();

        board.Archived.Should().BeEmpty();
        parent.Children.Should().BeEmpty();
    }

    [Fact]
    public async Task The_archive_reads_most_recently_removed_first()
    {
        var older = Card(BoardColumn.Ready, order: 1, number: 1000);
        var newer = Card(BoardColumn.Ready, order: 2, number: 1001);

        older.DeletedAt = Now.AddDays(-5);
        newer.DeletedAt = Now.AddDays(-1);

        var board = await BoardOf(older, newer);

        board.Archived.Select(card => card.Number).Should().Equal(1001, 1000);
        board.HasArchived.Should().BeTrue();
    }

    [Fact]
    public async Task A_duplicate_is_a_new_card_carrying_only_the_intent()
    {
        var card = Card(BoardColumn.Completed, order: 1);
        card.SessionId = "a-session";
        card.Badge = Badge.ReadyForReview;
        card.Metrics = new CardMetrics { TurnCount = 7 };

        var board = await BoardOf(card);

        var copy = await board.DuplicateAsync(card);

        copy.Id.Should().NotBe(card.Id);
        copy.Column.Should().Be(BoardColumn.Preparing);
        copy.SessionId.Should().BeNull();
        copy.Badge.Should().BeNull();
        copy.Metrics.Should().BeNull();
        board.All.Should().HaveCount(2);
    }

    [Fact]
    public async Task Every_mutation_announces_itself_once()
    {
        var card = Card(BoardColumn.Preparing, order: 1);
        var board = await BoardOf(card);
        var announcements = 0;

        board.Changed += () => announcements++;

        await board.MoveAsync(card, BoardColumn.Ready);

        announcements.Should().Be(1);
    }

    // The spec's lineage consistency rule: both sides in one operation. One announcement is the
    // observable half of that — a queue pass or a re-render between the child's create and the
    // parent's update would see a child whose parent does not list it.
    [Fact]
    public async Task Linking_a_follow_up_writes_both_sides_and_announces_once()
    {
        var parent = Card(BoardColumn.Executing, order: 1);
        var board = await BoardOf(parent);
        var announcements = 0;

        var child = Card(BoardColumn.Ready, order: 0, number: 1001);
        child.ParentId = parent.Id;

        board.Changed += () => announcements++;

        await board.LinkAsync(parent.Id, child);

        announcements.Should().Be(1);
        board.In(BoardColumn.Ready).Should().ContainSingle().Which.Id.Should().Be(child.Id);

        var linked = board.Card(parent.Id)!;

        linked.Children.Should().ContainSingle().Which.Should().Be(child.Id);
        linked.Transitions.Should().ContainSingle()
            .Which.Reason.Should().Be(TransitionReason.SpawnedFollowUp);
        linked.Transitions.Single().Note.Should().Contain($"#{child.Number}").And.Contain(child.Title);
    }

    [Fact]
    public async Task Linking_under_a_parent_that_vanished_still_creates_the_child()
    {
        var board = await BoardOf(Card(BoardColumn.Ready, order: 1));

        var child = Card(BoardColumn.Ready, order: 0, number: 1001);

        await board.LinkAsync(Guid.NewGuid(), child);

        board.Card(child.Id).Should().NotBeNull();
    }

    // The debounced writer's shape: the mutation runs against the instance the board holds at write
    // time, not against whatever the caller read before — which is what lets a flush carry its
    // numbers onto a card another writer has replaced in the meantime.
    [Fact]
    public async Task A_batched_update_mutates_the_boards_own_instance_and_announces_once()
    {
        var one = Card(BoardColumn.Executing, order: 1, number: 1000);
        var two = Card(BoardColumn.Executing, order: 2, number: 1001);
        var board = await BoardOf(one, two);
        var announcements = 0;
        var mutated = new List<Card>();

        board.Changed += () => announcements++;

        await board.UpdateManyAsync([one.Id, two.Id, Guid.NewGuid()], mutated.Add);

        announcements.Should().Be(1);
        mutated.Should().HaveCount(2, "an id the board does not know is skipped");
        mutated[0].Should().BeSameAs(board.Card(one.Id));
        mutated[1].Should().BeSameAs(board.Card(two.Id));
    }

    [Fact]
    public async Task A_batched_update_with_nothing_to_write_stays_silent()
    {
        var board = await BoardOf(Card(BoardColumn.Ready, order: 1));
        var announcements = 0;

        board.Changed += () => announcements++;

        await board.UpdateManyAsync([], _ => { });

        announcements.Should().Be(0);
    }

    // The regression test for the concurrency fix. `BoardState` is a singleton written from one drain
    // task per live session, the transcript pump, the queue runner, the retention sweep and every
    // circuit — and it rebuilds an ordinary dictionary on every write. Unserialised, two writers
    // clearing and refilling it at once is not a stale read but a corrupted one: an
    // `InvalidOperationException` out of an event pump, which then stops and takes that card's
    // liveness with it.
    [Fact]
    public async Task Concurrent_writers_cannot_tear_the_board()
    {
        var cards = Enumerable.Range(0, 8)
            .Select(i => Card(BoardColumn.Ready, order: i + 1, number: 1000 + i))
            .ToArray();

        var board = await BoardOf(cards);

        var writers = cards.SelectMany(card => Enumerable.Range(0, 40).Select(_ => card))
            .Select(card => Task.Run(async () =>
            {
                card.Title = $"touched {Guid.NewGuid()}";

                await board.UpdateAsync(card);

                // Reading while others write is the other half of the race.
                board.In(BoardColumn.Ready).Should().HaveCount(8);
            }));

        var run = async () => await Task.WhenAll(writers);

        await run.Should().NotThrowAsync();

        board.All.Should().HaveCount(8);
        board.In(BoardColumn.Ready).Should().HaveCount(8);
    }

    private static async Task<BoardState> BoardOf(params Card[] cards)
    {
        var board = new BoardState(new FakeCardStore(cards), new FakeAttachmentStore(), new FrozenClock(Now));

        await board.LoadAsync();

        return board;
    }

    private static Card Card(BoardColumn column, int order, int number = 1000) => new()
    {
        Number = number,
        Title = $"Card {number}",
        Column = column,
        Order = order,
        WorkingDir = "/dev/act",
    };
}
