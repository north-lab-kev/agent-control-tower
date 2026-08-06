using Act.App.Components.Pages;
using Act.Core.Model;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// `TaskEditing` owns the rule and `NewTaskForm.ApplyTo` enforces it whatever the markup does. What is
// left for the markup to get wrong is which control it froze — and the split is deliberately uneven, so
// "disable everything past the launch" is the plausible wrong answer this pins against: the title names
// the card rather than the work, and the whole launch config is what the *next* launch will use.
public class TaskViewLockTests : ComponentTest
{
    [Fact]
    public async Task Nothing_on_a_draft_is_frozen()
    {
        var cut = await Open(BoardColumn.Ready);

        cut.FindAll("div.rz-alert").Should().BeEmpty();
        cut.Find("div.titlerow input").HasAttribute("readonly").Should().BeFalse();
        cut.Find("textarea").HasAttribute("readonly").Should().BeFalse();
        RadzenDom.IsDisabled(cut, "Agent").Should().BeFalse();
    }

    // Said once, at the top, rather than repeated beside every frozen control: the reason is one fact
    // about the card, not a property of each field.
    [Fact]
    public async Task A_launched_card_says_once_why_half_of_it_is_fixed()
    {
        var cut = await Open(BoardColumn.Executing);

        cut.FindAll("div.rz-alert").Should().ContainSingle()
            .Which.TextContent.Should().Contain("This task has launched");
    }

    // Frozen: they describe the work that was actually asked for, and a session already exists that was
    // started from them.
    [Fact]
    public async Task A_launched_card_freezes_what_the_run_was()
    {
        var cut = await Open(BoardColumn.Executing);

        cut.Find("textarea").HasAttribute("readonly").Should().BeTrue();
        cut.Find("div.dirrow input").HasAttribute("readonly").Should().BeTrue();
        RadzenDom.IsDisabled(cut, "Agent").Should().BeTrue();
        RadzenDom.IsDisabled(cut, "Schedule").Should().BeTrue();
        RadzenDom.IsDisabled(cut, "Git when done").Should().BeTrue();
    }

    // Still open: a model or a permission mode does not apply to the turn already running, but it is
    // what the next launch uses — which is the whole point of switching model on a failed card before
    // retrying it.
    [Fact]
    public async Task A_launched_card_leaves_the_next_launch_editable()
    {
        var cut = await Open(BoardColumn.YourTurn);

        cut.Find("div.titlerow input").HasAttribute("readonly").Should().BeFalse();
        RadzenDom.IsDisabled(cut, "Model").Should().BeFalse();
        RadzenDom.IsDisabled(cut, "Reasoning effort").Should().BeFalse();
        RadzenDom.IsDisabled(cut, "Permission mode").Should().BeFalse();
    }

    // Read-only rather than disabled: a launched card's prompt is the thing you most often come back to
    // read, and a disabled textarea is neither selectable nor legible.
    [Fact]
    public async Task The_prompt_stays_readable_after_it_is_fixed()
    {
        var textarea = (await Open(BoardColumn.Executing)).Find("textarea");

        textarea.HasAttribute("readonly").Should().BeTrue();
        textarea.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task A_launched_card_can_still_be_saved_and_archived()
    {
        var cut = await Open(BoardColumn.Executing);

        cut.FindAll("div.destructive button").Should().HaveCount(3, "archive, duplicate and save-as-template");
        cut.FindAll("button[type=submit]").Should().ContainSingle();
    }

    // Archived wins over launched — it freezes strictly more, and both sentences would be one too many
    // for a page nothing on which can be changed.
    [Fact]
    public async Task An_archived_card_says_the_archive_reason_rather_than_both()
    {
        var cut = await Archived();

        cut.FindAll("div.rz-alert").Should().ContainSingle()
            .Which.TextContent.Should().Contain("This task is archived");
    }

    // Absent rather than disabled: a greyed Save invites the user to look for what would re-enable it,
    // and nothing would.
    [Fact]
    public async Task An_archived_card_offers_no_save_and_nothing_to_archive_into()
    {
        var cut = await Archived();

        cut.FindAll("button[type=submit]").Should().BeEmpty();
        cut.FindAll("div.destructive button").Should().HaveCount(2, "duplicate and save-as-template survive");
    }

    [Fact]
    public async Task An_archived_card_freezes_every_field_including_the_ones_a_launch_leaves_open()
    {
        var cut = await Archived();

        cut.Find("div.titlerow input").HasAttribute("readonly").Should().BeTrue();
        RadzenDom.IsDisabled(cut, "Model").Should().BeTrue();
        RadzenDom.IsDisabled(cut, "Reasoning effort").Should().BeTrue();
        RadzenDom.IsDisabled(cut, "Permission mode").Should().BeTrue();
    }

    [Fact]
    public async Task An_archived_card_cannot_be_asked_for_a_title()
    {
        (await Archived()).FindAll("div.titlerow button").Should().BeEmpty();
    }

    // The attachment list outlives the run — only a purge removes the bytes — so a locked card still
    // lists its files. What it loses is every way to change the list.
    [Fact]
    public async Task A_locked_card_lists_its_files_but_offers_no_way_to_change_them()
    {
        var card = TaskViewTests.Card(BoardColumn.Executing);
        card.Attachments = [new TaskAttachment { FileName = "shot.png", Length = 2048 }];

        await Attachments.SaveAsync(card.Id, "shot.png", new MemoryStream([1, 2, 3]));
        await BoardWith(card);

        var cut = Show(card.Id);

        cut.Find("div.chips .chip .name").TextContent.Should().Be("shot.png");
        cut.FindAll("div.dropzone").Should().BeEmpty();
        cut.FindAll("div.chip button").Should().ContainSingle("only the open button survives");
    }

    [Fact]
    public async Task A_locked_card_with_no_files_says_so_rather_than_showing_a_drop_zone()
    {
        var cut = await Open(BoardColumn.Executing);

        cut.FindAll("div.dropzone").Should().BeEmpty();
        cut.Markup.Should().Contain("No attachments");
    }

    private async Task<IRenderedComponent<TaskView>> Open(BoardColumn column)
    {
        var card = TaskViewTests.Card(column);

        await BoardWith(card);

        return Show(card.Id);
    }

    private async Task<IRenderedComponent<TaskView>> Archived()
    {
        var card = TaskViewTests.Card(BoardColumn.Completed);
        card.DeletedAt = Now.AddDays(-1);

        await BoardWith(card);

        return Show(card.Id);
    }

    private IRenderedComponent<TaskView> Show(Guid cardId)
        => Render<TaskView>(p => p.Add(c => c.CardId, cardId));
}
