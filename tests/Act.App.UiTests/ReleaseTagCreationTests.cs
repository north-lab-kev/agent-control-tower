using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Act.App.UiTests;

// The release tag is created through GitHub's API rather than pushed with git, because a push
// authenticated as `GITHUB_TOKEN` is refused whenever the pre-receive hook reads it as creating or
// updating anything under `.github/workflows/` — which a tag push can trip, and which no permission
// on that token can allow. Nothing compiles against the workflow and a release is manual, slow and
// public, so the shape of that step is pinned here: a revert to `git push` would surface only as a
// failed release after both platforms had already built, and a slip to a lightweight tag would
// surface only as wrong dates on the public Tags page.
public class ReleaseTagCreationTests
{
    [Fact]
    public void The_workflow_never_pushes_to_the_remote()
        => Commands().Should().NotContain("git push");

    [Fact]
    public void The_tag_object_is_created_against_the_commit_being_released()
    {
        var step = CreateTagStep();

        step.Should().Contain("git/tags");
        step.Should().Contain("-f tag=\"$env:TAG\"");
        step.Should().Contain("-f object=\"$env:GITHUB_SHA\"");
        step.Should().Contain("-f type=commit");
    }

    [Fact]
    public void The_tag_object_carries_the_version_as_its_message()
        => CreateTagStep().Should().Contain("-f message=\"ACT $env:TAG\"");

    [Fact]
    public void The_tag_ref_points_at_the_tag_object_so_the_tag_stays_annotated()
    {
        var step = CreateTagStep();

        step.Should().MatchRegex(@"\$object\s*=\s*gh api\s+""repos/\$env:GITHUB_REPOSITORY/git/tags""");
        step.Should().Contain("git/refs");
        step.Should().Contain("-f ref=\"refs/tags/$env:TAG\"");
        step.Should().Contain("-f sha=\"$object\"");
    }

    // `shell: pwsh` reports a native command's failure only at the end of the step, so a rejected
    // tag-object call would otherwise fall through and create the ref against an empty sha.
    [Fact]
    public void A_tag_object_that_comes_back_without_a_sha_fails_the_step()
        => CreateTagStep().Should().MatchRegex(@"if \(-not \$object\) \{\s*\r?\n\s*throw");

    [Fact]
    public void The_tag_step_is_authenticated()
        => CreateTagStep().Should().Contain("GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}");

    [Fact]
    public void The_release_job_may_write_contents()
        => ReleaseJob().Should().Contain("contents: write");

    // Two independent guards stop one version being cut twice, and either is easy to lose in an
    // edit: the gate refuses a tag that already exists before anything is built, and `--verify-tag`
    // refuses to publish a release whose tag never made it.
    [Fact]
    public void The_gate_refuses_a_version_that_is_already_tagged()
    {
        var commands = Commands();

        commands.Should().Contain("git tag --list $env:TAG");
        commands.Should().Contain("already exists");
    }

    [Fact]
    public void The_release_refuses_a_tag_that_does_not_exist()
        => Commands().Should().Contain("--verify-tag");

    private static string CreateTagStep()
    {
        var match = Regex.Match(
            ReleaseWorkflowFile.Text(),
            @"- name: Create tag\r?\n(?<body>.*?)(?=^\s*- name: )",
            RegexOptions.Singleline | RegexOptions.Multiline);

        match.Success.Should().BeTrue("the release workflow must have a Create tag step");

        return match.Groups["body"].Value;
    }

    private static string ReleaseJob()
    {
        var workflow = ReleaseWorkflowFile.Text();
        var start = workflow.IndexOf("\n  release:", StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, "the workflow must have a release job");

        return workflow[start..];
    }

    // Comment lines are dropped so that prose about a command — including the note on this very
    // file explaining why `git push` is gone — cannot satisfy or defeat an assertion about it.
    private static string Commands()
        => string.Join(
            '\n',
            ReleaseWorkflowFile.Text()
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith('#')));
}
