using Act.Core.Model;
using Act.Core.Spawning;
using AwesomeAssertions;

namespace Act.Core.Tests;

// What the read tools are allowed to say. The exclusions are the point: `list_tasks` and `get_task`
// are the first thing ACT ever tells an agent, and the two fields left out are left out because a
// path is a grant and a session id is ACT's own join key.
public class TaskProjectionTests
{
    private static readonly Guid Caller = Guid.NewGuid();

    [Fact]
    public void A_summary_says_where_a_card_stands_and_nothing_heavier()
    {
        var summary = TaskSummary.From(Card(), Caller);

        summary.Number.Should().Be(1039);
        summary.Title.Should().Be("Rename the widget");
        summary.Column.Should().Be("your_turn");
        summary.Badge.Should().Be("to_review");
        summary.Agent.Should().Be("claude");
    }

    [Fact]
    public void A_card_knows_whether_the_caller_is_the_one_that_spawned_it()
    {
        var mine = Card();
        mine.ParentId = Caller;

        TaskSummary.From(mine, Caller).SpawnedByYou.Should().BeTrue();
        TaskSummary.From(Card(), Caller).SpawnedByYou.Should().BeFalse();
    }

    [Fact]
    public void Detail_carries_the_prompt_the_settings_and_the_lineage()
    {
        var card = Card();
        var child = Guid.NewGuid();
        var prerequisite = Guid.NewGuid();

        card.Children.Add(child);
        card.DependsOn.Add(prerequisite);
        card.AutoGit = new AutoGitOptions { Action = GitAction.PullRequest };

        var detail = TaskDetail.From(card, Caller);

        detail.Prompt.Should().Be("Do the thing.");
        detail.WorkingDir.Should().Be("C:/repo");
        detail.Model.Should().Be("sonnet");
        detail.Permission.Should().Be("acceptEdits");
        detail.Schedule.Should().Be("manual");
        detail.AutoGit.Should().Be("pr");
        detail.Origin.Should().Be("manual");
        detail.Children.Should().ContainSingle().Which.Should().Be(child.ToString());
        detail.DependsOn.Should().ContainSingle().Which.Should().Be(prerequisite.ToString());
    }

    // A title is what an agent recognises a task by, and with title writing switched off a card can be
    // stored without one — so both projections name it the way every screen does, from the prompt.
    [Fact]
    public void An_untitled_card_is_still_named_for_the_agent()
    {
        var card = Card();
        card.Title = string.Empty;

        TaskSummary.From(card, Caller).Title.Should().Be("Do the thing");
        TaskDetail.From(card, Caller).Title.Should().Be("Do the thing");
    }

    // Not an omission to tidy up later: the session id is the key ACT's own ingestion joins on, and
    // an attachment path is a directory outside the working tree that the launch had to grant
    // explicitly. Neither is an agent's to read.
    [Fact]
    public void Detail_never_carries_the_session_id_or_an_attachment_path()
    {
        var card = Card();
        card.SessionId = "019fbb34-secret";
        card.Attachments.Add(new TaskAttachment { FileName = "spec.pdf" });

        var detail = TaskDetail.From(card, Caller);

        detail.GetType().GetProperties().Select(property => property.Name)
            .Should().NotContain(["SessionId", "Attachments"]);

        detail.ToString().Should().NotContain("019fbb34-secret").And.NotContain("spec.pdf");
    }

    // The vocabulary is a published API an agent writes back into `create_followup`, so it must not
    // move when an enum member is renamed.
    [Theory]
    [InlineData(BoardColumn.YourTurn, "your_turn")]
    [InlineData(BoardColumn.Completed, "completed")]
    [InlineData(BoardColumn.Preparing, "preparing")]
    public void The_column_words_are_stable(BoardColumn column, string expected)
        => TaskWords.Of(column).Should().Be(expected);

    [Fact]
    public void The_badge_words_are_stable()
    {
        TaskWords.Of(Badge.ReadyForReview).Should().Be("to_review");
        TaskWords.Of(Badge.NeedsPermission).Should().Be("needs_permission");
        TaskWords.Of((Badge?)null).Should().BeNull();
    }

    private static Card Card() => new()
    {
        Id = Guid.NewGuid(),
        Number = 1039,
        Title = "Rename the widget",
        InitialPrompt = "Do the thing.",
        WorkingDir = "C:/repo",
        AgentType = AgentType.ClaudeCode,
        Column = BoardColumn.YourTurn,
        Badge = Model.Badge.ReadyForReview,
        LaunchConfig = new LaunchConfig
        {
            Model = "sonnet",
            PermissionMode = PermissionMode.AcceptEdits,
        },
    };
}

public class TaskListingTests
{
    [Fact]
    public void A_board_inside_the_cap_is_reported_whole()
    {
        var listing = TaskListing.Of(Summaries(3));

        listing.Tasks.Should().HaveCount(3);
        listing.Total.Should().Be(3);
        listing.Truncated.Should().BeFalse();
    }

    // A quietly short list reads exactly like a complete one, and the duplicate the agent was
    // checking for would be in the part that was dropped.
    [Fact]
    public void A_board_past_the_cap_says_so_rather_than_trimming_in_silence()
    {
        var listing = TaskListing.Of(Summaries(TaskListing.MaxTasks + 40));

        listing.Tasks.Should().HaveCount(TaskListing.MaxTasks);
        listing.Total.Should().Be(TaskListing.MaxTasks + 40);
        listing.Truncated.Should().BeTrue();
    }

    private static List<TaskSummary> Summaries(int count)
        => [.. Enumerable.Range(0, count).Select(index => new TaskSummary(
            Guid.NewGuid().ToString(),
            1000 + index,
            $"Task {index}",
            "ready",
            null,
            "claude",
            false))];
}
