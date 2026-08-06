using Act.App.Components.Pages;
using Act.App.Components.Shared;
using Act.Core.Model;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// Every move inside ACT — Cancel, the back arrow, the face tabs, the top bar, the browser's own back
// button — arrives as a location change and can simply be refused. The dirty check is one `!=` against a
// snapshot, so what these pin is the *plumbing*: that the guard is registered at all, that the two exits
// which settle the edits do not then ask about them, and that Escape closes an open picker before it
// starts leaving.
public class TaskViewExitTests : ComponentTest
{
    [Fact]
    public async Task A_clean_form_leaves_without_a_word()
    {
        var cut = await Open();

        cut.Find("header.bar button").Click();

        Route.Should().BeEmpty();
        DialogsOpened.Should().BeEmpty();
    }

    [Fact]
    public async Task A_dirty_form_asks_before_it_goes()
    {
        var cut = await Open();

        cut.Find("div.titlerow input").Change("Something else");
        cut.Find("header.bar button").Click();

        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());
        Route.Should().Be(Edit, "the navigation is held while the question stands");
    }

    [Fact]
    public async Task Staying_keeps_the_page_and_the_edits()
    {
        var cut = await Open();

        cut.Find("div.titlerow input").Change("Something else");
        cut.Find("header.bar button").Click();

        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, false);

        Route.Should().Be(Edit);
        TaskViewTests.Title(cut).Should().Be("Something else");
    }

    // What "discard" has to mean: the store keeps what it had. Whether the held navigation then resumes is
    // Blazor's business rather than this page's — the page's job was to not write the edit.
    [Fact]
    public async Task Discarding_writes_nothing()
    {
        var cut = await Open();

        cut.Find("div.titlerow input").Change("Something else");
        cut.Find("header.bar button").Click();

        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, true);

        Board.Card(Card.Id)!.Title.Should().Be("Rename the widget");
    }

    // Set by the two exits that make the edits moot, so leaving does not then ask about them.
    [Fact]
    public async Task A_save_settles_the_edits_and_leaves_for_the_board()
    {
        var cut = await Open();

        cut.Find("div.titlerow input").Change("Renamed");

        await cut.Find("form").SubmitAsync();

        Route.Should().BeEmpty();
        DialogsOpened.Should().BeEmpty();
        Board.Card(Card.Id)!.Title.Should().Be("Renamed");
    }

    [Fact]
    public async Task An_archive_settles_them_too()
    {
        var cut = await Open();

        cut.Find("div.titlerow input").Change("Renamed");
        cut.Find("div.destructive button").Click();

        cut.WaitForAssertion(() => Route.Should().BeEmpty());
        DialogsOpened.Should().BeEmpty("a childless card needs no confirmation — the archive page is the undo");
        Board.Card(Card.Id)!.IsDeleted.Should().BeTrue();
    }

    // Archiving is reversible, so a childless card needs no confirmation. A card with follow-ups is a
    // different thing: their fate is a real choice about tasks other than this one.
    [Fact]
    public async Task A_card_with_follow_ups_asks_what_happens_to_them()
    {
        var (cut, _) = await OpenWithFollowUp();

        cut.Find("div.destructive button").Click();

        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle()
            .Which.Dialog.Should().Be<ArchiveFollowUpsDialog>());

        Board.Card(Card.Id)!.IsDeleted.Should().BeFalse("nothing moves until the question is answered");
    }

    [Fact]
    public async Task Keeping_the_follow_ups_archives_only_the_card()
    {
        var (cut, child) = await OpenWithFollowUp();

        cut.Find("div.destructive button").Click();
        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, FollowUpChoice.KeepFollowUps);

        cut.WaitForAssertion(() => Board.Card(Card.Id)!.IsDeleted.Should().BeTrue());
        Board.Card(child.Id)!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Archiving_with_the_follow_ups_takes_them_along()
    {
        var (cut, child) = await OpenWithFollowUp();

        cut.Find("div.destructive button").Click();
        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, FollowUpChoice.WithFollowUps);

        cut.WaitForAssertion(() => Board.Card(child.Id)!.IsDeleted.Should().BeTrue());
    }

    [Fact]
    public async Task Cancelling_the_question_leaves_everything_where_it_was()
    {
        var (cut, child) = await OpenWithFollowUp();

        cut.Find("div.destructive button").Click();
        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, FollowUpChoice.Cancel);

        Board.Card(Card.Id)!.IsDeleted.Should().BeFalse();
        Board.Card(child.Id)!.IsDeleted.Should().BeFalse();
        Route.Should().Be(Edit);
    }

    // Dismissed with the X or the overlay, which arrives as null and means the same as Cancel.
    [Fact]
    public async Task Dismissing_the_question_means_cancel()
    {
        var (cut, child) = await OpenWithFollowUp();

        cut.Find("div.destructive button").Click();
        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, null);

        Board.Card(Card.Id)!.IsDeleted.Should().BeFalse();
        Board.Card(child.Id)!.IsDeleted.Should().BeFalse();
    }

    // The *saved* card is what gets copied, not the form, and the copy opens on its own route — where
    // anything different about it would be plainly visible rather than silently carried over.
    [Fact]
    public async Task Duplicating_copies_the_saved_card_and_opens_the_copy()
    {
        var cut = await Open();

        cut.FindAll("div.destructive button")[1].Click();

        cut.WaitForAssertion(() => Board.All.Should().HaveCount(2));

        var copy = Board.All.Single(card => card.Id != Card.Id);

        copy.Title.Should().Be("Copy of Rename the widget");
        copy.Column.Should().Be(BoardColumn.Preparing);
        Route.Should().Be($"card/{copy.Id}/edit");
    }

    // The form rather than the saved card, unlike Duplicate: a template is not a run, so it is the shape of
    // what is on screen. The name is asked for because nothing else on the form describes a *kind* of task.
    [Fact]
    public async Task A_template_is_captured_from_the_form_and_needs_a_name()
    {
        var cut = await Open();

        cut.Find("div.dirrow input").Change("/dev/elsewhere");
        cut.FindAll("div.destructive button")[2].Click();

        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle()
            .Which.Dialog.Should().Be<TemplateNameDialog>());

        await AnswerDialog(cut, "Nightly sweep");

        cut.WaitForAssertion(() => Settings.Templates.Should().HaveCount(2));

        var saved = Settings.Templates.Single(template => !template.IsDefault);

        saved.Name.Should().Be("Nightly sweep");
        saved.WorkingDir.Should().Be("/dev/elsewhere", "the form is what was captured, not the stored card");
    }

    [Fact]
    public async Task A_template_nobody_named_is_not_saved()
    {
        var cut = await Open();

        cut.FindAll("div.destructive button")[2].Click();
        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, null);

        Settings.Templates.Should().ContainSingle("only the default");
    }

    // Offered on an unsaved task too: a template is made of the fields on the form rather than of a card, so
    // there is nothing to save first.
    [Fact]
    public void A_template_can_be_captured_from_a_task_that_was_never_saved()
    {
        var cut = Render<TaskView>();

        cut.FindAll("div.destructive button").Should().ContainSingle("no card yet, so only save-as-template");
    }

    // The folder picker is part of this page rather than a popup of its own, so Escape has to close it here —
    // leaving the page out from under an open picker is not what the key was pressed for.
    [Fact]
    public async Task Escape_closes_an_open_picker_before_it_leaves()
    {
        Directories.With("/dev/act", "src");

        var cut = await Open();

        cut.Find("div.dirrow button").Click();
        cut.FindAll("div.picker").Should().ContainSingle();

        await Escape(cut);

        cut.FindAll("div.picker").Should().BeEmpty();
        Route.Should().Be(Edit, "the first Escape only closed the picker");

        await Escape(cut);

        Route.Should().BeEmpty();
    }

    [Fact]
    public async Task Escape_on_a_page_with_nothing_open_leaves()
    {
        var cut = await Open();

        await Escape(cut);

        Route.Should().BeEmpty();
    }

    // Unsaved edits still get their question, because leaving is a navigation like any other.
    [Fact]
    public async Task Escape_still_asks_about_unsaved_edits()
    {
        var cut = await Open();

        cut.Find("div.titlerow input").Change("Something else");

        await Escape(cut);

        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());
        Route.Should().Be(Edit);
    }

    // The picked path is absolute and real, so it replaces whatever was typed rather than being merged with
    // it, and the picker closes — it has done its job.
    [Fact]
    public async Task A_picked_folder_replaces_whatever_was_typed()
    {
        Directories.With("/dev/act", "src").With("/dev/act/src", "Act.Core");

        var cut = await Open();

        cut.Find("div.dirrow button").Click();
        cut.Find("button.entry").Click();
        cut.Find("div.pickeractions button:last-child").Click();

        cut.FindAll("div.picker").Should().BeEmpty("the picker has done its job");
        TaskViewTests.WorkingDir(cut).Should().Be("/dev/act/src");
    }

    private static Task Escape(IRenderedComponent<TaskView> cut)
        => cut.InvokeAsync(() => cut.FindComponent<EscapeKey>().Instance.Escaped());

    private Card Card { get; set; } = default!;

    private string Edit => $"card/{Card.Id}/edit";

    // Navigated to first, so the page is on the url it would really be on — which is what makes "the
    // navigation was held" a meaningful assertion rather than a comparison against the base address.
    private async Task<IRenderedComponent<TaskView>> Open()
    {
        Card = TaskViewTests.Card(BoardColumn.Ready);

        await BoardWith(Card);

        Navigation.NavigateTo(Edit);

        return Render<TaskView>(p => p.Add(c => c.CardId, Card.Id));
    }

    private async Task<(IRenderedComponent<TaskView> Cut, Card Child)> OpenWithFollowUp()
    {
        Card = TaskViewTests.Card(BoardColumn.Ready);

        var child = TaskViewTests.Card(BoardColumn.Preparing);
        child.Number = 1043;
        child.Title = "Fix the sweep";
        child.ParentId = Card.Id;
        child.Origin = TaskOrigin.Spawned;

        Card.Children = [child.Id];

        await BoardWith(Card, child);

        Navigation.NavigateTo(Edit);

        return (Render<TaskView>(p => p.Add(c => c.CardId, Card.Id)), child);
    }
}
