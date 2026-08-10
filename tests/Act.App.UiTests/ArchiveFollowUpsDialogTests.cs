using Act.App.Components.Shared;
using Act.Core.Model;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// The one prompt in ACT that is a real dialog, because it decides the fate of tasks other than the one
// on screen. Three buttons, three distinct answers: this is where the three-way is pinned, since
// `TaskView` only ever sees the enum it is handed and would archive the wrong set silently if two
// buttons agreed.
//
// Opened through `DialogService` rather than rendered bare, so the answer travels the route the caller
// actually awaits.
public class ArchiveFollowUpsDialogTests : ComponentTest
{
    [Fact]
    public void The_follow_ups_are_named_rather_than_counted()
    {
        var rows = Dialog(FollowUp(7, "Fix the sweep"), FollowUp(8, "Update the notes"))
            .Host.WaitForElements("ul.kids li");

        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("7").And.Contain("Fix the sweep");
        rows[1].TextContent.Should().Contain("8").And.Contain("Update the notes");
    }

    [Fact]
    public void The_question_says_how_many_there_are()
    {
        Dialog(FollowUp(7, "Fix the sweep"), FollowUp(8, "Update the notes"))
            .Host.WaitForElement("div.askfollowups").TextContent
            .Should().Contain("2 follow-up tasks").And.Contain("Archive them too?");
    }

    // A single follow-up is the common case — a task usually spawns one — so it is the wording most
    // users see, and `follow-up task(s)` was what they saw before.
    [Fact]
    public void One_follow_up_asks_in_the_singular()
    {
        Dialog(FollowUp(7, "Fix the sweep"))
            .Host.WaitForElement("div.askfollowups").TextContent
            .Should().Contain("1 follow-up task.").And.Contain("Archive it too?");
    }

    // Position is only how the buttons are found; what is pinned is that each closes with its own
    // answer, and that none of them closes with another's.
    [Theory]
    [InlineData(0, FollowUpChoice.Cancel)]
    [InlineData(1, FollowUpChoice.KeepFollowUps)]
    [InlineData(2, FollowUpChoice.WithFollowUps)]
    public async Task Each_button_closes_with_its_own_answer(int button, FollowUpChoice expected)
    {
        var closed = await Dialog(FollowUp(7, "Fix the sweep")).ClosedWith("div.actions button", button);

        closed.Should().Be(expected);
    }

    private DialogRun<ArchiveFollowUpsDialog> Dialog(params Card[] followUps)
        => OpenDialog<ArchiveFollowUpsDialog>(
            (nameof(ArchiveFollowUpsDialog.Card), new Card { Number = 1042, Title = "Rename the widget" }),
            (nameof(ArchiveFollowUpsDialog.FollowUps), followUps));

    private static Card FollowUp(int number, string title) => new()
    {
        Number = number,
        Title = title,
        Column = BoardColumn.Preparing,
        Origin = TaskOrigin.Spawned,
    };
}
