using Act.App.Cards;
using Act.App.Settings;
using Act.Core.Agents;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// The save writes the prompt's opening words and leaves; this is the half that pays the debt. Two
// properties carry the feature: the strip is pending exactly while a query runs, and the *only*
// title a backfill may replace is the placeholder it was started with — anything the user typed in
// the meantime is theirs.
public class TitleBackfillTests
{
    private readonly MockAgentAdapter adapter = new();

    private readonly FakeCardStore store = new([]);

    private readonly BoardState board;

    private readonly TitleBackfill backfill;

    public TitleBackfillTests()
    {
        board = new BoardState(store, new FakeAttachmentStore(), new FrozenClock(DateTimeOffset.UtcNow));

        var settings = new UserSettingsService(new FakeSettingsStore(), new AppCulture());
        var titles = new TaskTitles([adapter], settings, NullLogger<TaskTitles>.Instance);

        backfill = new TitleBackfill(titles, board, NullLogger<TitleBackfill>.Instance);
    }

    [Fact]
    public async Task The_agents_answer_replaces_the_placeholder()
    {
        adapter.Answer = "Rename the widget";

        var card = await Saved();

        backfill.Start(card);

        await backfill.Idle;

        board.Card(card.Id)!.Title.Should().Be("Rename the widget");
        backfill.IsPending(card.Id).Should().BeFalse();
    }

    [Fact]
    public async Task The_card_is_pending_exactly_while_the_query_runs()
    {
        adapter.QueryHeld = new TaskCompletionSource();

        var card = await Saved();

        backfill.Start(card);

        backfill.IsPending(card.Id).Should().BeTrue();
        board.Card(card.Id)!.Title.Should().Be("Rename the widget everywhere");

        adapter.QueryHeld.SetResult();

        await backfill.Idle;

        backfill.IsPending(card.Id).Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_query_leaves_the_placeholder_and_stops_the_spinner()
    {
        adapter.QueryFails = new InvalidOperationException("claude is not on PATH");

        var card = await Saved();

        backfill.Start(card);

        await backfill.Idle;

        board.Card(card.Id)!.Title.Should().Be("Rename the widget everywhere");
        backfill.IsPending(card.Id).Should().BeFalse();
    }

    [Fact]
    public async Task An_unusable_answer_writes_nothing()
    {
        adapter.Answer = null;

        var card = await Saved();
        store.Updated.Clear();

        backfill.Start(card);

        await backfill.Idle;

        store.Updated.Should().BeEmpty();
        board.Card(card.Id)!.Title.Should().Be("Rename the widget everywhere");
    }

    [Fact]
    public async Task An_answer_that_matches_the_placeholder_writes_nothing()
    {
        adapter.Answer = "Rename the widget everywhere";

        var card = await Saved();
        store.Updated.Clear();

        backfill.Start(card);

        await backfill.Idle;

        store.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task A_title_the_user_typed_in_the_meantime_is_kept()
    {
        adapter.QueryHeld = new TaskCompletionSource();
        adapter.Answer = "Rename the widget";

        var card = await Saved();

        backfill.Start(card);

        await board.UpdateManyAsync([card.Id], renamed => renamed.Title = "My own words");

        adapter.QueryHeld.SetResult();

        await backfill.Idle;

        board.Card(card.Id)!.Title.Should().Be("My own words");
    }

    [Fact]
    public async Task Cancelling_clears_pending_and_the_answer_never_lands()
    {
        adapter.QueryHeld = new TaskCompletionSource();
        adapter.Answer = "Rename the widget";

        var card = await Saved();

        backfill.Start(card);
        backfill.Cancel(card.Id);

        backfill.IsPending(card.Id).Should().BeFalse();

        adapter.QueryHeld.SetResult();

        board.Card(card.Id)!.Title.Should().Be("Rename the widget everywhere");
    }

    [Fact]
    public async Task A_second_start_for_the_same_card_supersedes_the_first()
    {
        adapter.QueryHeld = new TaskCompletionSource();
        adapter.Answer = "Rename the widget";

        var card = await Saved();

        backfill.Start(card);
        backfill.Start(card);

        adapter.QueryHeld.SetResult();

        await backfill.Idle;

        adapter.Queries.Should().HaveCount(2);
        board.Card(card.Id)!.Title.Should().Be("Rename the widget");
        backfill.IsPending(card.Id).Should().BeFalse();
    }

    [Fact]
    public async Task A_card_with_no_prompt_starts_nothing()
    {
        var card = await Saved(prompt: "   ");

        backfill.Start(card);

        backfill.IsPending(card.Id).Should().BeFalse();
        adapter.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_settled_run_announces_itself()
    {
        adapter.QueryFails = new InvalidOperationException("claude is not on PATH");

        var card = await Saved();
        var announced = 0;

        backfill.Changed += () => Interlocked.Increment(ref announced);

        backfill.Start(card);

        await backfill.Idle;

        announced.Should().BeGreaterThanOrEqualTo(2, "once when the run starts and once when it settles");
        backfill.IsPending(card.Id).Should().BeFalse();
    }

    private async Task<Card> Saved(string prompt = "Rename the widget everywhere")
    {
        var card = new Card
        {
            Title = string.IsNullOrWhiteSpace(prompt) ? "Untitled" : TaskTitleQuery.FromPrompt(prompt),
            InitialPrompt = prompt,
            AgentType = adapter.Agent,
            Column = BoardColumn.Preparing,
        };

        await board.CreateAsync(card);

        return card;
    }
}
