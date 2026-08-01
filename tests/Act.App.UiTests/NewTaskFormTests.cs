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
