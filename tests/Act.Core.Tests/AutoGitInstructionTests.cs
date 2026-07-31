using System.Globalization;
using Act.Core.Agents;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Core.Tests;

// Every case pins the UI culture explicitly. The wording is localised, so a test that relied on
// the ambient culture would pass or fail depending on the machine it ran on.
public class AutoGitInstructionTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");

    [Fact]
    public void A_task_without_git_keeps_its_prompt_verbatim()
        => In(English, () => AutoGitInstruction.Append("do the thing", null).Should().Be("do the thing"));

    [Theory]
    [InlineData(GitAction.Commit, false, "Once you are done: commit your work.")]
    [InlineData(GitAction.Push, false, "Once you are done: commit and push your work.")]
    [InlineData(GitAction.PullRequest, false, "Once you are done: commit, push and open a pull request.")]
    [InlineData(GitAction.PullRequest, true, "Once you are done: commit, push and create a draft pull request.")]
    public void The_configured_git_work_is_spelled_out(GitAction action, bool draft, string expected)
        => In(English, () => AutoGitInstruction.For(new AutoGitOptions { Action = action, Draft = draft })
            .Should().Be(expected));

    // The agent is told to do the work in the language the user runs ACT in, so this is the
    // assertion that the suffix goes through resources rather than a literal.
    [Theory]
    [InlineData(GitAction.Commit, false, "Quand tu as terminé : commite ton travail.")]
    [InlineData(GitAction.Push, false, "Quand tu as terminé : commite et pousse ton travail.")]
    [InlineData(GitAction.PullRequest, false, "Quand tu as terminé : commite, pousse et ouvre une pull request.")]
    [InlineData(GitAction.PullRequest, true, "Quand tu as terminé : commite, pousse et crée une pull request brouillon.")]
    public void The_instruction_follows_the_ui_language(GitAction action, bool draft, string expected)
        => In(French, () => AutoGitInstruction.For(new AutoGitOptions { Action = action, Draft = draft })
            .Should().Be(expected));

    // `draft` is a property of a pull request and nothing else, so it must not leak into the
    // wording of an action that cannot express it.
    [Fact]
    public void Draft_is_ignored_for_an_action_that_is_not_a_pull_request()
        => In(English, () => AutoGitInstruction.For(new AutoGitOptions { Action = GitAction.Push, Draft = true })
            .Should().NotContain("draft"));

    [Fact]
    public void The_instruction_is_appended_after_the_prompt_not_before_it()
        => In(English, () =>
        {
            var composed = AutoGitInstruction.Append(
                "do the thing",
                new AutoGitOptions { Action = GitAction.Commit });

            composed.Should().StartWith("do the thing");
            composed.Should().EndWith("Once you are done: commit your work.");
        });

    private static void In(CultureInfo culture, Action assert)
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;

        try
        {
            assert();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
