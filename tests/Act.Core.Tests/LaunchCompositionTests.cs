using Act.Core.Agents;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class LaunchCompositionTests
{
    [Fact]
    public void The_task_keeps_what_describes_the_work()
    {
        var composed = LaunchComposition.Compose(Task(), Machine());

        composed.Model.Should().Be("opus");
        composed.Effort.Should().Be("high");
        composed.PermissionMode.Should().Be(PermissionMode.AcceptEdits);
    }

    [Fact]
    public void The_machine_supplies_what_describes_the_install()
    {
        var composed = LaunchComposition.Compose(Task(), Machine());

        composed.AgentBinary.Should().Be(@"C:\tools\codex.exe");
        composed.ExtraFlags.Should().Equal("--verbose");
        composed.Env.Should().ContainKey("HTTPS_PROXY");
    }

    // The reason this clears rather than merges. Cards written before the settings existed carry
    // their own copy of these three, and reading one back would resurrect a path the user has since
    // corrected in the one place it now lives.
    [Fact]
    public void A_cards_own_stale_copy_never_reaches_the_adapter()
    {
        var stale = Task();
        stale.AgentBinary = @"C:\old\codex.exe";
        stale.ExtraFlags = ["--from-the-card"];
        stale.Env = new Dictionary<string, string> { ["OLD"] = "1" };

        var composed = LaunchComposition.Compose(stale, Machine());

        composed.AgentBinary.Should().Be(@"C:\tools\codex.exe");
        composed.ExtraFlags.Should().Equal("--verbose");
        composed.Env.Should().NotContainKey("OLD");
    }

    [Fact]
    public void With_no_settings_for_the_agent_the_install_fields_are_empty()
    {
        var stale = Task();
        stale.AgentBinary = @"C:\old\codex.exe";

        var composed = LaunchComposition.Compose(stale, null);

        composed.AgentBinary.Should().BeNull();
        composed.ExtraFlags.Should().BeEmpty();
        composed.Env.Should().BeEmpty();
    }

    // Empty is how a user says "just find it on PATH", and it must reach the adapter as null rather
    // than as a whitespace path it would then try to spawn.
    [Fact]
    public void A_blank_binary_reads_as_no_binary()
    {
        var composed = LaunchComposition.Compose(Task(), new AgentDefaults { Binary = "   " });

        composed.AgentBinary.Should().BeNull();
    }

    [Fact]
    public void Composing_does_not_mutate_the_cards_own_config()
    {
        var task = Task();

        LaunchComposition.Compose(task, Machine());

        task.AgentBinary.Should().BeNull();
        task.ExtraFlags.Should().BeEmpty();
    }

    private static LaunchConfig Task() => new()
    {
        Model = "opus",
        Effort = "high",
        PermissionMode = PermissionMode.AcceptEdits,
    };

    private static AgentDefaults Machine() => new()
    {
        Agent = AgentType.Codex,
        Binary = @"C:\tools\codex.exe",
        ExtraFlags = ["--verbose"],
        Env = new Dictionary<string, string> { ["HTTPS_PROXY"] = "http://proxy:8080" },
    };
}
