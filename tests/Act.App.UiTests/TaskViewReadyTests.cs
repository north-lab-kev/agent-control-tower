using Act.App.Components.Pages;
using Act.Core.Model;
using AwesomeAssertions;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Act.App.UiTests;

// The form's own door into the queue: save, and put the card where the runner can pick it up. It is the
// board's Preparing → Ready drag said as a button, so it obeys the same rule — `ManualMove` allows that
// move and no other — and it is offered on nothing that has already left Preparing.
public class TaskViewReadyTests : ComponentTest
{
    [Fact]
    public async Task A_create_saved_as_ready_lands_in_the_queue_rather_than_in_preparing()
    {
        var cut = Show();

        Fill(cut, prompt: "Rename the widget everywhere", workingDir: "/dev/act");

        await Ready(cut);

        Board.In(BoardColumn.Ready).Should().ContainSingle()
            .Which.WorkingDir.Should().Be("/dev/act");
        Board.In(BoardColumn.Preparing).Should().BeEmpty();
        Route.Should().BeEmpty();
    }

    [Fact]
    public async Task A_create_saved_the_ordinary_way_still_starts_in_preparing()
    {
        var cut = Show();

        Fill(cut, prompt: "Rename the widget everywhere", workingDir: "/dev/act");

        await cut.Find("form").SubmitAsync();

        Board.In(BoardColumn.Preparing).Should().ContainSingle();
        Board.In(BoardColumn.Ready).Should().BeEmpty();
    }

    // A title is still written from the prompt on the fast path: the shortcut is about where the card
    // lands, not about saving less of it.
    [Fact]
    public async Task A_create_saved_as_ready_still_gets_a_title()
    {
        Claude.Answer = "Rename the widget";

        var cut = Show();

        Fill(cut, prompt: "Rename the widget everywhere", workingDir: "/dev/act");

        await Ready(cut);
        await Backfill.Idle;

        Board.All.Should().ContainSingle().Which.Title.Should().Be("Rename the widget");
    }

    // The same button on a draft that was saved earlier, which is the other half of "still preparing" —
    // and the move is recorded as the hand move it is, because a button the user pressed is a decision
    // and not an automated transition.
    [Fact]
    public async Task A_draft_edited_and_saved_as_ready_moves_and_says_who_moved_it()
    {
        var card = TaskViewTests.Card(BoardColumn.Preparing);

        await BoardWith(card);

        var cut = Render<TaskView>(p => p.Add(c => c.CardId, card.Id));

        Fill(cut, title: "Rename the widget properly");

        await Ready(cut);

        var saved = Board.In(BoardColumn.Ready).Should().ContainSingle().Subject;

        saved.Title.Should().Be("Rename the widget properly");
        saved.Transitions.Should().ContainSingle()
            .Which.Reason.Should().Be(TransitionReason.MovedByHand);
    }

    // Not a submit button, so the form's validators have to be asked rather than arriving with the
    // event. Nothing may reach the queue that an ordinary save would have refused.
    [Fact]
    public async Task A_form_the_validators_refuse_reaches_neither_column()
    {
        var cut = Show();

        Fill(cut, workingDir: "/dev/act");

        await Ready(cut);

        Board.All.Should().BeEmpty();
        cut.Markup.Should().Contain("Write a title");
    }

    // A task that does not exist yet is created, not saved, and the button says the word the user is
    // about to make true.
    [Fact]
    public void A_create_names_both_buttons_after_what_they_make()
    {
        var cut = Show();

        RadzenDom.ButtonText(cut.Find("div.foot button.save")).Should().Be("Create");
        RadzenDom.ButtonText(cut.Find("div.foot button.ready")).Should().Be("Create + Ready");
    }

    [Fact]
    public async Task An_edit_saves_rather_than_creates()
    {
        var cut = await Open(BoardColumn.Preparing);

        RadzenDom.ButtonText(cut.Find("div.foot button.save")).Should().Be("Save");
        RadzenDom.ButtonText(cut.Find("div.foot button.ready")).Should().Be("Save + Ready");
    }

    // The plain save first, the queue door second and filled: reading left to right, the further
    // button is the one that does more, and it is the one the page expects to be pressed.
    [Fact]
    public void The_queue_door_sits_after_the_plain_save_and_carries_the_weight()
    {
        var actions = Show().FindAll("div.foot button.save, div.foot button.ready");

        actions[0].ClassList.Should().Contain("save");
        actions[1].ClassList.Should().Contain("ready");

        RadzenDom.IsFilled(actions[0]).Should().BeFalse();
        RadzenDom.IsFilled(actions[1]).Should().BeTrue();
    }

    // The queue door does not wait on the CLI either: the card is in Ready with the prompt's opening
    // words while the query is still out, and the answer lands on the board afterwards.
    [Fact]
    public async Task The_queue_door_returns_before_the_title_arrives()
    {
        Claude.QueryHeld = new TaskCompletionSource();
        Claude.Answer = "Rename the widget";

        var cut = Show();

        Fill(cut, prompt: "Rename the widget everywhere", workingDir: "/dev/act");

        await Ready(cut);

        var saved = Board.In(BoardColumn.Ready).Should().ContainSingle().Subject;

        saved.Title.Should().Be("Rename the widget everywhere");
        Backfill.IsPending(saved.Id).Should().BeTrue();

        Claude.QueryHeld.SetResult();

        await Backfill.Idle;

        Board.Card(saved.Id)!.Title.Should().Be("Rename the widget");
    }

    [Fact]
    public async Task A_card_already_in_the_queue_is_offered_no_way_back_into_it()
    {
        (await Open(BoardColumn.Ready)).FindAll("button.ready").Should().BeEmpty();
    }

    [Fact]
    public async Task A_launched_card_is_offered_no_way_into_the_queue()
    {
        (await Open(BoardColumn.Executing)).FindAll("button.ready").Should().BeEmpty();
    }

    [Fact]
    public async Task An_archived_card_is_offered_no_way_into_the_queue()
    {
        var card = TaskViewTests.Card(BoardColumn.Preparing);

        card.DeletedAt = Now.AddDays(-1);

        await BoardWith(card);

        Render<TaskView>(p => p.Add(c => c.CardId, card.Id)).FindAll("button.ready").Should().BeEmpty();
    }

    private static Task Ready(IRenderedComponent<TaskView> cut)
        => cut.Find("button.ready").ClickAsync(new MouseEventArgs());

    private async Task<IRenderedComponent<TaskView>> Open(BoardColumn column)
    {
        var card = TaskViewTests.Card(column);

        await BoardWith(card);

        return Render<TaskView>(p => p.Add(c => c.CardId, card.Id));
    }

    private static void Fill(
        IRenderedComponent<TaskView> cut,
        string? title = null,
        string? prompt = null,
        string? workingDir = null)
    {
        if (title is not null)
            cut.Find("div.titlerow input").Change(title);

        if (prompt is not null)
            cut.Find("textarea").Change(prompt);

        if (workingDir is not null)
            cut.Find("div.dirrow input").Change(workingDir);
    }

    private IRenderedComponent<TaskView> Show() => Render<TaskView>();
}
