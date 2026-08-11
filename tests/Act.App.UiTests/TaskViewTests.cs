using Act.App.Components.Pages;
using Act.Core.Model;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// One component serves both routes: `CardId` null is a create, set is an edit. So the shape of the page
// is the first thing worth pinning — which heading, which starting values, and the not-found branch a
// route can reach with a stale id that a modal opened from a card in hand never could.
public class TaskViewTests : ComponentTest
{
    [Fact]
    public void A_create_opens_on_the_default_template()
    {
        Settings.SaveTemplate(Template("Nightly sweep", "/dev/nightly", AgentType.Codex));

        var cut = Show();

        cut.Find("header.bar .title").TextContent.Should().Be("New task");
        cut.FindAll("header.bar .id").Should().BeEmpty("a card that does not exist yet has no number");
        WorkingDir(cut).Should().BeEmpty();
    }

    // A query parameter rather than a route of its own: it narrows `/card/new` rather than naming a
    // different destination.
    [Fact]
    public void A_create_from_a_template_starts_from_that_template()
    {
        var template = Template("Nightly sweep", "/dev/nightly", AgentType.Codex);

        Settings.SaveTemplate(template);
        Navigation.NavigateTo($"/card/new?template={template.Id}");

        var cut = Show();

        WorkingDir(cut).Should().Be("/dev/nightly");
        RadzenDom.Selected(cut, "Agent").Should().Be("codex");
    }

    // An id that no longer exists falls back to the default rather than 404ing a page whose whole job is
    // to make a task.
    [Fact]
    public void A_template_that_is_gone_falls_back_to_the_default()
    {
        Settings.SaveTemplate(Template("Nightly sweep", "/dev/nightly", AgentType.Codex));
        Navigation.NavigateTo($"/card/new?template={Guid.NewGuid()}");

        var cut = Show();

        WorkingDir(cut).Should().BeEmpty();
        cut.Find("header.bar .title").TextContent.Should().Be("New task");
    }

    // The commonest sequence of creates is several tasks against the same repository, so the folder —
    // unlike the title and the prompt — is worth carrying from one create to the next.
    [Fact]
    public async Task A_created_tasks_folder_pre_fills_the_next_create()
    {
        var first = Show();

        first.Find("textarea").Change("Do the thing");
        first.Find("div.dirrow input").Change("/dev/act");

        await first.Find("form").SubmitAsync();
        await Backfill.Idle;

        WorkingDir(Show()).Should().Be("/dev/act");
    }

    [Fact]
    public void A_template_that_names_a_folder_wins_over_the_last_used_one()
    {
        Settings.SetLastWorkingDir("/dev/act");

        var template = Template("Nightly sweep", "/dev/nightly", AgentType.Codex);

        Settings.SaveTemplate(template);
        Navigation.NavigateTo($"/card/new?template={template.Id}");

        WorkingDir(Show()).Should().Be("/dev/nightly");
    }

    [Fact]
    public void A_template_that_leaves_the_folder_blank_falls_back_to_the_last_used_one()
    {
        Settings.SetLastWorkingDir("/dev/act");

        var template = Template("Nightly sweep", string.Empty, AgentType.Codex);

        Settings.SaveTemplate(template);
        Navigation.NavigateTo($"/card/new?template={template.Id}");

        WorkingDir(Show()).Should().Be("/dev/act");
    }

    // The remembered folder is the *created* task's, so an edit — whatever it changes — moves nothing.
    [Fact]
    public async Task Editing_a_card_does_not_change_the_remembered_folder()
    {
        Settings.SetLastWorkingDir("/dev/act");

        var card = Card(BoardColumn.Ready);

        await BoardWith(card);

        var edit = Show(card.Id);

        edit.Find("div.dirrow input").Change("/dev/elsewhere");

        await edit.Find("form").SubmitAsync();

        Settings.LastWorkingDir.Should().Be("/dev/act");
    }

    // The pre-fill lands before the dirty baseline is taken, so a create opened and immediately left
    // has nothing to ask about.
    [Fact]
    public void A_pre_filled_create_is_not_dirty()
    {
        Settings.SetLastWorkingDir("/dev/act");

        var cut = Show();

        WorkingDir(cut).Should().Be("/dev/act");

        cut.Find("header.bar button").Click();

        DialogsOpened.Should().BeEmpty();
        Route.Should().BeEmpty();
    }

    // The board is read synchronously first, which is what makes the heading right on the *first* render
    // rather than "New task" corrected a frame later.
    [Fact]
    public async Task An_edit_opens_on_the_card()
    {
        var card = Card(BoardColumn.Ready);

        await BoardWith(card);

        var cut = Show(card.Id);

        cut.Find("header.bar .title").TextContent.Should().Be("Rename the widget");
        cut.Find("header.bar .id").TextContent.Should().Be("#1042");
        WorkingDir(cut).Should().Be("/dev/act");
        Prompt(cut).Should().Be("Rename the widget everywhere");
    }

    [Fact]
    public void The_prompt_opens_tall_enough_to_draft_in()
    {
        Show().Find("textarea").GetAttribute("rows").Should().Be("14");
    }

    [Fact]
    public void A_create_starts_with_the_caret_in_the_prompt()
    {
        Show();

        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task An_editable_card_starts_with_the_caret_in_the_prompt()
    {
        var card = Card(BoardColumn.Ready);

        await BoardWith(card);

        Show(card.Id);

        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task An_edit_can_switch_to_the_card_other_faces()
    {
        var card = Card(BoardColumn.Ready);

        await BoardWith(card);

        Show(card.Id).FindAll("nav.cardtabs a.tab").Should().HaveCount(3);
    }

    [Fact]
    public void A_create_has_no_other_face_to_switch_to()
    {
        Show().FindAll("nav.cardtabs").Should().BeEmpty();
    }

    // A bookmark, or the back button after an archive: the id is real and the card is not.
    [Fact]
    public void An_id_that_is_not_there_says_so_and_offers_nothing_to_edit()
    {
        var cut = Show(Guid.NewGuid());

        cut.Find("div.taskview.empty").TextContent.Should().Contain("no longer exists");
        cut.FindAll("form").Should().BeEmpty();
        cut.FindAll("div.row").Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelling_goes_back_where_the_card_lives()
    {
        var card = Card(BoardColumn.Ready);

        await BoardWith(card);

        Show(card.Id).Find("header.bar button").Click();

        Route.Should().BeEmpty("a live card belongs to the board");
    }

    // An archived card was opened *from* the archive, so back is the archive — see `CardExit`.
    [Fact]
    public async Task An_archived_card_goes_back_to_the_archive()
    {
        var card = Card(BoardColumn.Completed);
        card.DeletedAt = Now.AddDays(-1);

        await BoardWith(card);

        Show(card.Id).Find("header.bar button").Click();

        Route.Should().Be("archive");
    }

    // Both boxes carry their text as a `value` attribute rather than as content — Blazor writes the
    // attribute, and a `textarea` read through `TextContent` is empty however full the field looks.
    internal static string WorkingDir(IRenderedComponent<TaskView> cut)
        => cut.Find("div.dirrow input").GetAttribute("value") ?? string.Empty;

    internal static string Title(IRenderedComponent<TaskView> cut)
        => cut.Find("div.titlerow input").GetAttribute("value") ?? string.Empty;

    internal static string Prompt(IRenderedComponent<TaskView> cut)
        => cut.Find("textarea").GetAttribute("value") ?? string.Empty;

    internal IRenderedComponent<TaskView> Show(Guid? cardId = null)
        => Render<TaskView>(p =>
        {
            if (cardId is { } id)
                p.Add(c => c.CardId, id);
        });

    // With an id of its own, because `TaskTemplate` does not mint one and the query parameter is how a
    // create finds it.
    internal static TaskTemplate Template(string name, string workingDir, AgentType agent) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        WorkingDir = workingDir,
        Agent = agent,
        Prompt = "Do the thing",
    };

    internal static Card Card(BoardColumn column) => new()
    {
        Number = 1042,
        Title = "Rename the widget",
        InitialPrompt = "Rename the widget everywhere",
        Column = column,
        AgentType = AgentType.ClaudeCode,
        WorkingDir = "/dev/act",
        Schedule = TaskSchedule.Manual,
    };
}
