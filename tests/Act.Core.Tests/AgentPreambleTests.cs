using Act.Core.Agents;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class AgentPreambleTests
{
    private static readonly Guid TaskId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    [Fact]
    public void It_teaches_the_status_convention()
    {
        var preamble = AgentPreamble.Compose(TaskId);

        preamble.Should().Contain(ActContract.RelativeStatusDirectory);
        preamble.Should().Contain(ActContract.FilePrefix(TaskId));
        preamble.Should().Contain("ready_for_review");
        preamble.Should().Contain("needs_input");
    }

    [Fact]
    public void It_teaches_the_follow_up_convention()
    {
        var preamble = AgentPreamble.Compose(TaskId);

        preamble.Should().Contain(ActContract.RelativeFollowUpsDirectory);
        preamble.Should().Contain("\"title\"");
        preamble.Should().Contain("\"prompt\"");
        preamble.Should().Contain("\"cwd\"");
        preamble.Should().Contain("\"dependsOn\"");
    }

    [Fact]
    public void It_names_the_missing_status_file_fallback()
        => AgentPreamble.Compose(TaskId).Should().Contain("without this file");

    [Fact]
    public void It_asks_for_atomic_writes()
        => AgentPreamble.Compose(TaskId).Should().Contain("rename");

    [Fact]
    public void It_stays_silent_about_git_when_no_git_is_configured()
        => AgentPreamble.Compose(TaskId).Should().NotContain("### Git");

    [Theory]
    [InlineData(GitAction.Commit, false, "commit your work")]
    [InlineData(GitAction.Push, false, "commit and push your work")]
    [InlineData(GitAction.PullRequest, false, "commit, push, and open a pull request")]
    [InlineData(GitAction.PullRequest, true, "commit, push, and open a draft pull request")]
    public void It_spells_out_the_configured_git_work(GitAction action, bool draft, string expected)
    {
        var preamble = AgentPreamble.Compose(TaskId, new AutoGitOptions { Action = action, Draft = draft });

        preamble.Should().Contain("### Git");
        preamble.Should().Contain(expected);
    }

    [Fact]
    public void Failed_git_is_routed_back_to_the_user_rather_than_completed()
        => AgentPreamble.Compose(TaskId, new AutoGitOptions { Action = GitAction.Push })
            .Should().Contain("If any git step fails");
}
