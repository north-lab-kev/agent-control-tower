using Act.App.Cards;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.App.UiTests;

// The save path, which is where the launch-boundary gate has to hold: disabling an input is a
// courtesy to the user, but the rule is that a launched card's prompt cannot change, and only the
// code that writes the card can promise that.
public class NewTaskFormTests
{
    [Fact]
    public void A_card_that_has_not_launched_takes_every_field()
    {
        var card = CardIn(BoardColumn.Ready);

        Edited().ApplyTo(card);

        card.Title.Should().Be("New title");
        card.InitialPrompt.Should().Be("something else entirely");
        card.WorkingDir.Should().Be("C:/other");
        card.AgentType.Should().Be(AgentType.Codex);
        card.Schedule.Should().Be(TaskSchedule.NextWindow);
        card.AutoGit!.Action.Should().Be(GitAction.PullRequest);
        card.LaunchConfig.Model.Should().Be("opus");
    }

    [Theory]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    [InlineData(BoardColumn.Completed)]
    public void A_launched_card_keeps_what_it_ran_with(BoardColumn column)
    {
        var card = CardIn(column);

        Edited().ApplyTo(card);

        card.InitialPrompt.Should().Be("do the thing");
        card.WorkingDir.Should().Be("C:/repo");
        card.AgentType.Should().Be(AgentType.ClaudeCode);
        card.Schedule.Should().Be(TaskSchedule.Manual);
        card.AutoGit.Should().BeNull();
    }

    // The other half, and the reason the gate is a split rather than a freeze: switching model on a
    // failed card is how the user retries it differently, and the title names the card rather than
    // the work.
    [Fact]
    public void A_launched_card_still_takes_its_title_and_launch_config()
    {
        var card = CardIn(BoardColumn.YourTurn);

        Edited().ApplyTo(card);

        card.Title.Should().Be("New title");
        card.LaunchConfig.Model.Should().Be("opus");
        card.LaunchConfig.PermissionMode.Should().Be(PermissionMode.AcceptEdits);
    }

    // The title is optional to *type*, never optional to *have*. The page normally fills it in by
    // asking the agent, but that goes out to a CLI which can be missing, unauthenticated or out of
    // quota — so the guarantee lives in the code that writes the card, where nothing can route around
    // it. An untitled strip is unrecognisable and unsearchable, and no screen repairs it.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void A_blank_title_is_written_from_the_prompt_rather_than_stored_empty(string title)
    {
        var card = CardIn(BoardColumn.Ready);

        (Edited() with { Title = title, Prompt = "Consolidate the badge colours across the css files" })
            .ApplyTo(card);

        card.Title.Should().Be("Consolidate the badge colours across the css files");
    }

    // Same rule past the launch boundary, where the prompt is frozen but the title is not: an edit
    // that clears the title must not be the way to strand a running card without a name.
    [Fact]
    public void A_launched_card_whose_title_is_cleared_is_renamed_from_its_prompt()
    {
        var card = CardIn(BoardColumn.Executing);

        // The prompt the form carries for a launched card is the card's own, read-only copy.
        (Edited() with { Title = "  ", Prompt = "do the thing" }).ApplyTo(card);

        card.Title.Should().Be("do the thing");
        card.InitialPrompt.Should().Be("do the thing");
    }

    [Fact]
    public void A_typed_title_always_wins_over_the_prompt()
    {
        var card = CardIn(BoardColumn.Ready);

        (Edited() with { Title = "  My own words  " }).ApplyTo(card);

        card.Title.Should().Be("My own words");
    }

    // A template that names its tasks is the point of having a title on one: the form opens with it
    // already in the box, so it is a *typed* title as far as the save is concerned and wins over the
    // prompt.
    [Fact]
    public void A_template_title_reaches_the_card()
    {
        var card = CardIn(BoardColumn.Ready);

        NewTaskForm.From(new TaskTemplate { Title = "Nightly dependency sweep", Prompt = "Update the packages" })
            .ApplyTo(card);

        card.Title.Should().Be("Nightly dependency sweep");
    }

    // Blank is the old behaviour and has to stay reachable: without a title the card is named from
    // the prompt, which is what a task with nothing typed already gets.
    [Fact]
    public void A_template_with_no_title_leaves_the_card_named_from_its_prompt()
    {
        var card = CardIn(BoardColumn.Ready);

        NewTaskForm.From(new TaskTemplate { Prompt = "Update the packages" }).ApplyTo(card);

        card.Title.Should().Be("Update the packages");
    }

    // The write side of the same field. A template is the shape of the form in front of you, so the
    // title travels with everything else — and the template's own *name* is a separate thing.
    [Fact]
    public void Saving_a_template_from_a_form_captures_its_title()
    {
        var template = (Edited() with { Title = "  Fix the login bug  " }).ToTemplate("  Bugfix  ");

        template.Name.Should().Be("Bugfix");
        template.Title.Should().Be("Fix the login bug");
        template.Prompt.Should().Be("something else entirely");
    }

    // A moment is not a habit: a template holding one would arm every task it made for a time that
    // has already passed.
    [Fact]
    public void Saving_a_template_drops_a_specific_datetime_schedule()
    {
        var form = Edited() with
        {
            Schedule = TaskSchedule.SpecificDateTime,
            ScheduledFor = new DateTime(2026, 1, 1, 9, 0, 0),
        };

        form.ToTemplate("Bugfix").Schedule.Should().Be(TaskSchedule.Manual);
    }

    // The id is the attachment directory's name, so a create has to stage into the folder the saved
    // card will own — a `ToCard` that minted a fresh one would leave every attached file orphaned.
    [Fact]
    public void The_saved_card_keeps_the_id_the_form_staged_its_files_under()
    {
        var form = Edited();

        form.ToCard(DateTimeOffset.UnixEpoch).Id.Should().Be(form.CardId);
    }

    [Fact]
    public void A_card_that_has_not_launched_takes_its_attachments()
    {
        var card = CardIn(BoardColumn.Ready);

        (Edited() with { Attachments = [Attached("spec.md")] }).ApplyTo(card);

        card.Attachments.Should().ContainSingle().Which.FileName.Should().Be("spec.md");
    }

    // Frozen with the prompt, and for the same reason: the files are part of what the run was handed.
    [Fact]
    public void A_launched_card_keeps_the_attachments_it_ran_with()
    {
        var card = CardIn(BoardColumn.Executing);
        card.Attachments = [Attached("trace.log")];

        (Edited() with { Attachments = [Attached("spec.md")] }).ApplyTo(card);

        card.Attachments.Should().ContainSingle().Which.FileName.Should().Be("trace.log");
    }

    [Fact]
    public void An_edited_cards_attachments_arrive_on_the_form()
    {
        var card = CardIn(BoardColumn.Ready);
        card.Attachments = [Attached("spec.md")];

        var form = NewTaskForm.From(card);

        form.CardId.Should().Be(card.Id);
        form.Attachments.Should().ContainSingle().Which.FileName.Should().Be("spec.md");
    }

    // A file belongs to one run, exactly like the specific datetime a template already drops — so a
    // task started from a template begins with an empty attachment list and its own fresh folder.
    [Fact]
    public void A_task_made_from_a_template_starts_with_no_attachments()
    {
        var form = NewTaskForm.From(new TaskTemplate { Prompt = "Update the packages" });

        form.Attachments.Should().BeEmpty();
        form.CardId.Should().NotBe(Guid.Empty);
    }

    private static TaskAttachment Attached(string name) => new() { FileName = name, Length = 12 };

    private static NewTaskForm Edited() => new()
    {
        Title = "New title",
        Prompt = "something else entirely",
        WorkingDir = "C:/other",
        Agent = AgentType.Codex,
        Model = "opus",
        Permission = PermissionMode.AcceptEdits,
        Schedule = TaskSchedule.NextWindow,
        SelectedGitAction = GitAction.PullRequest,
    };

    private static Card CardIn(BoardColumn column) => new()
    {
        Title = "A task",
        Column = column,
        InitialPrompt = "do the thing",
        WorkingDir = "C:/repo",
        AgentType = AgentType.ClaudeCode,
        Schedule = TaskSchedule.Manual,
    };
}
