using Act.Core.Model;
using Act.Core.Spawning;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.App.UiTests;

// The one routine that turns a `create_followup` call into a card. `FollowUpResolverTests` covers what
// the *arguments* mean; what is left here is everything that needs a board behind it — the budget, the
// lineage written on both sides, the parent's timeline row, and the two reads.
//
// It matters that this tier exists at all: the browser spec drives the happy path end to end, but a
// refusal leaves nothing on screen to assert against, and the cap is not something a browser test can
// reach without creating a hundred cards.
public class FollowUpServiceTests : ComponentTest
{
    private static readonly Guid ParentId = Guid.NewGuid();

    [Fact]
    public async Task A_follow_up_becomes_a_ready_card_owned_by_its_parent()
    {
        await BoardWith(Parent());

        var outcome = await FollowUps.CreateAsync(ParentId, Ask());

        outcome.Refusals.Should().BeEmpty();

        var child = Board.In(BoardColumn.Ready).Should().ContainSingle().Which;

        child.Title.Should().Be("Migrate the auth tests");
        child.ParentId.Should().Be(ParentId);
        child.Origin.Should().Be(TaskOrigin.Spawned);
        child.SpawnAuthor.Should().Be(SpawnAuthor.Agent);
        outcome.Created!.Number.Should().Be(child.Number);
        outcome.Created.Id.Should().Be(child.Id.ToString());
    }

    // Stored on both sides, so both sides have to be written in the one operation — a child pointing at
    // a parent that does not list it is the drift the spec's consistency rule exists to prevent, and it
    // would break the archive's follow-ups dialog and the session rail at once.
    [Fact]
    public async Task The_lineage_is_written_on_both_sides()
    {
        await BoardWith(Parent());

        await FollowUps.CreateAsync(ParentId, Ask());

        var child = Board.In(BoardColumn.Ready).Single();

        Board.Card(ParentId)!.Children.Should().ContainSingle().Which.Should().Be(child.Id);
        Board.ChildrenOf(Board.Card(ParentId)!).Should().ContainSingle().Which.Id.Should().Be(child.Id);
    }

    // No column, because the parent did not move — it produced something. `TimelineEntry` renders a
    // reasoned row with a null column as wording alone, which is what makes that legal.
    [Fact]
    public async Task The_parent_gets_a_timeline_row_that_names_the_child_and_moves_nothing()
    {
        var parent = Parent();
        parent.Column = BoardColumn.Executing;

        await BoardWith(parent);

        await FollowUps.CreateAsync(ParentId, Ask());

        var child = Board.In(BoardColumn.Ready).Single();
        var row = Board.Card(ParentId)!.Transitions
            .Should().ContainSingle(entry => entry.Reason == TransitionReason.SpawnedFollowUp).Which;

        row.Column.Should().BeNull();
        row.Badge.Should().BeNull();
        row.At.Should().Be(Now);
        row.Note.Should().Contain($"#{child.Number}").And.Contain("Migrate the auth tests");

        Board.Card(ParentId)!.Column.Should().Be(BoardColumn.Executing, "producing a card is not a move");
    }

    [Fact]
    public async Task Each_spawn_adds_its_own_row()
    {
        await BoardWith(Parent());

        await FollowUps.CreateAsync(ParentId, Ask());
        await FollowUps.CreateAsync(ParentId, Ask() with { Title = "And another" });

        Board.Card(ParentId)!.Transitions
            .Count(entry => entry.Reason == TransitionReason.SpawnedFollowUp)
            .Should().Be(2);
    }

    // The runaway-loop backstop, and the reason it is worth a test at this tier: no browser spec is
    // going to create a hundred cards to reach it.
    [Fact]
    public async Task The_cap_refuses_further_follow_ups_and_creates_nothing()
    {
        await BoardWith(Parent());

        Settings.SetMaxFollowUpsPerCard(2);

        await FollowUps.CreateAsync(ParentId, Ask());
        await FollowUps.CreateAsync(ParentId, Ask() with { Title = "Second" });

        var refused = await FollowUps.CreateAsync(ParentId, Ask() with { Title = "Third" });

        refused.Created.Should().BeNull();
        refused.Refusals.Should().ContainSingle().Which.Should().Contain("limit of 2");
        Board.In(BoardColumn.Ready).Should().HaveCount(2);
    }

    // A refusal an agent reads and can act on: it has to say what to do next, or the model's only move
    // is to try the same call again.
    [Fact]
    public async Task The_cap_refusal_tells_the_agent_what_to_do_instead()
    {
        await BoardWith(Parent());

        Settings.SetMaxFollowUpsPerCard(0);

        var refused = await FollowUps.CreateAsync(ParentId, Ask());

        refused.Refusals.Single().Should().Contain("settings").And.Contain("this task");
    }

    [Fact]
    public async Task A_call_from_a_task_that_no_longer_exists_is_refused()
    {
        await BoardWith(Parent());

        var refused = await FollowUps.CreateAsync(Guid.NewGuid(), Ask());

        refused.Created.Should().BeNull();
        refused.Refusals.Should().ContainSingle();
        Board.In(BoardColumn.Ready).Should().BeEmpty();
    }

    [Fact]
    public async Task A_bad_argument_is_refused_without_touching_the_board()
    {
        await BoardWith(Parent());

        var refused = await FollowUps.CreateAsync(ParentId, Ask() with { Agent = "gemini" });

        refused.Created.Should().BeNull();
        Board.In(BoardColumn.Ready).Should().BeEmpty();
        Board.Card(ParentId)!.Children.Should().BeEmpty();
        Board.Card(ParentId)!.Transitions.Should().BeEmpty();
    }

    // Read-then-write races; a key does not. The second call must hand back the *first* card — not a
    // twin, and not a refusal.
    [Fact]
    public async Task A_repeated_client_key_returns_the_first_card_rather_than_a_second_one()
    {
        await BoardWith(Parent());

        var first = await FollowUps.CreateAsync(ParentId, Ask() with { ClientKey = "retry-1" });
        var second = await FollowUps.CreateAsync(ParentId, Ask() with { ClientKey = "retry-1" });

        second.Created!.Id.Should().Be(first.Created!.Id);
        second.Created.AlreadyExisted.Should().BeTrue();
        first.Created.AlreadyExisted.Should().BeFalse();

        Board.In(BoardColumn.Ready).Should().ContainSingle();
        Board.Card(ParentId)!.Children.Should().ContainSingle();
    }

    // Scoped to the caller, so two tasks using the same obvious key ("1") do not collide.
    [Fact]
    public async Task A_client_key_belongs_to_the_task_that_used_it()
    {
        var other = Parent();
        other.Id = Guid.NewGuid();
        other.Number = 1040;

        await BoardWith(Parent(), other);

        await FollowUps.CreateAsync(ParentId, Ask() with { ClientKey = "1" });
        await FollowUps.CreateAsync(other.Id, Ask() with { ClientKey = "1" });

        Board.In(BoardColumn.Ready).Should().HaveCount(2);
    }

    // A key spent on a refused call must not be burnt: nothing was created, so the retry has to be
    // allowed to create it.
    [Fact]
    public async Task A_client_key_from_a_refused_call_is_still_usable()
    {
        await BoardWith(Parent());

        await FollowUps.CreateAsync(ParentId, Ask() with { ClientKey = "k", Agent = "gemini" });

        var second = await FollowUps.CreateAsync(ParentId, Ask() with { ClientKey = "k" });

        second.Created.Should().NotBeNull();
        Board.In(BoardColumn.Ready).Should().ContainSingle();
    }

    // The cap is spent inside the gate, so two calls arriving together cannot both see the last slot.
    [Fact]
    public async Task Concurrent_calls_cannot_both_take_the_last_slot()
    {
        await BoardWith(Parent());

        Settings.SetMaxFollowUpsPerCard(1);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(index =>
                FollowUps.CreateAsync(ParentId, Ask() with { Title = $"Follow-up {index}" })));

        results.Count(result => result.Created is not null).Should().Be(1);
        Board.In(BoardColumn.Ready).Should().ContainSingle();
        Board.Card(ParentId)!.Children.Should().ContainSingle();
    }

    [Fact]
    public async Task List_reports_the_whole_board_newest_first()
    {
        await BoardWith(Parent(), Ready(1041, "Older"), Ready(1042, "Newer"));

        var listing = FollowUps.List(ParentId, column: null);

        listing.Total.Should().Be(3);
        listing.Truncated.Should().BeFalse();
        listing.Tasks.Select(task => task.Number).Should().ContainInOrder(1042, 1041, 1039);
    }

    [Fact]
    public async Task List_can_be_filtered_to_one_column()
    {
        await BoardWith(Parent(), Ready(1041, "Waiting"));

        FollowUps.List(ParentId, "ready").Tasks.Should().ContainSingle()
            .Which.Number.Should().Be(1041);

        FollowUps.List(ParentId, "executing").Tasks.Should().BeEmpty();
    }

    // An unrecognised filter is not an error — it means "no filter". The alternative is a refusal for a
    // typo in an optional argument, on a read that is safe to answer in full.
    [Fact]
    public async Task An_unrecognised_filter_falls_back_to_the_whole_board()
    {
        await BoardWith(Parent(), Ready(1041, "Waiting"));

        FollowUps.List(ParentId, "somewhere-else").Tasks.Should().HaveCount(2);
    }

    // A card the user has put away is not live work, and pointing an agent at one would send it after
    // something that is not on the board.
    [Fact]
    public async Task Neither_read_can_see_an_archived_card()
    {
        var archived = Ready(1041, "Put away");
        archived.ArchivedAt = Now;

        await BoardWith(Parent(), archived);

        FollowUps.List(ParentId, column: null).Tasks.Should().ContainSingle()
            .Which.Number.Should().Be(1039);

        FollowUps.Detail(ParentId, archived.Id.ToString()).Should().BeNull();
    }

    [Fact]
    public async Task The_caller_can_tell_its_own_children_from_everything_else()
    {
        await BoardWith(Parent(), Ready(1041, "Not mine"));

        await FollowUps.CreateAsync(ParentId, Ask());

        var mine = FollowUps.List(ParentId, column: null).Tasks
            .Where(task => task.SpawnedByYou)
            .Should().ContainSingle().Which;

        mine.Title.Should().Be("Migrate the auth tests");
    }

    [Fact]
    public async Task Detail_answers_for_a_real_id_and_nothing_else()
    {
        await BoardWith(Parent());

        FollowUps.Detail(ParentId, ParentId.ToString())!.Number.Should().Be(1039);
        FollowUps.Detail(ParentId, Guid.NewGuid().ToString()).Should().BeNull();
    }

    // The id comes from a model, so it can be anything at all. A malformed one is an empty answer, not
    // an exception the agent sees as a broken server.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("the second one")]
    [InlineData("1039")]
    public async Task Detail_answers_nothing_for_an_id_that_is_not_one(string id)
    {
        await BoardWith(Parent());

        FollowUps.Detail(ParentId, id).Should().BeNull();
    }

    private static FollowUpRequest Ask() => new("Migrate the auth tests", "Port them to the new fixture.");

    private static Card Parent() => new()
    {
        Id = ParentId,
        Number = 1039,
        Title = "Rework the auth module",
        InitialPrompt = "Do the work.",
        Column = BoardColumn.YourTurn,
        AgentType = AgentType.ClaudeCode,
        WorkingDir = "/dev/act",
        LaunchConfig = new LaunchConfig { Model = MockAgentAdapter.FastModel },
        CreatedAt = Now,
    };

    private static Card Ready(int number, string title) => new()
    {
        Id = Guid.NewGuid(),
        Number = number,
        Title = title,
        Column = BoardColumn.Ready,
        AgentType = AgentType.ClaudeCode,
        WorkingDir = "/dev/act",
        CreatedAt = Now,
    };
}
