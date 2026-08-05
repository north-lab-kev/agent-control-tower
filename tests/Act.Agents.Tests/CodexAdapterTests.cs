using Act.Agents.Codex;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.Agents.Tests;

public class CodexAdapterContractTests : AgentAdapterContract
{
    protected override IAgentAdapter CreateAdapter()
        => new CodexAdapter(new StubPtyHost(), new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<CodexAdapter>.Instance);
}

// The Codex-specific facts, pinned so a CLI upgrade that moves them fails here rather than at
// launch. Every assertion below is a documented flag of `codex-cli 0.146.0-alpha.3.1`.
public class CodexAdapterTests
{
    private static readonly Guid TaskId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    [Fact]
    public async Task A_launch_has_no_session_id_because_Codex_mints_its_own()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty);

        session.SessionId.Should().BeNull();
        session.TaskId.Should().Be(TaskId);
        pty.Last.Arguments.Should().NotContain("--session-id");
    }

    // The correlation handle that stands in until `SessionStart` reports the real id — and it
    // rides the environment rather than the command line so Codex's hook-trust hash is stable.
    [Fact]
    public async Task A_launch_carries_the_task_id_on_the_environment()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty);

        pty.Last.Environment.Should().ContainKey(AgentEnvironment.ActTaskId)
            .WhoseValue.Should().Be(TaskId.ToString("d"));
    }

    [Fact]
    public async Task The_opening_prompt_is_positional_and_is_the_task_text_alone()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty);

        pty.Last.Arguments[^1].Should().Be("do the thing");
    }

    [Fact]
    public async Task Effort_goes_through_the_config_override_not_a_flag()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(
            pty,
            new LaunchConfig { Model = "gpt-5.5", Effort = "xhigh" });

        pty.Last.Arguments.Should().NotContain("--effort");
        pty.Last.Arguments.Should().ContainInOrder("-c", "model_reasoning_effort=\"xhigh\"");
    }

    [Theory]
    [InlineData(PermissionMode.Plan, "never", "read-only")]
    [InlineData(PermissionMode.Default, "untrusted", "workspace-write")]
    [InlineData(PermissionMode.AcceptEdits, "on-request", "workspace-write")]
    [InlineData(PermissionMode.DontAsk, "never", "workspace-write")]
    public async Task Permission_modes_map_onto_approval_and_sandbox(
        PermissionMode mode,
        string approval,
        string sandbox)
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty, new LaunchConfig { PermissionMode = mode });

        pty.Last.Arguments.Should().ContainInOrder("--ask-for-approval", approval);
        pty.Last.Arguments.Should().ContainInOrder("--sandbox", sandbox);
    }

    [Fact]
    public async Task Bypass_uses_the_single_dangerous_flag_and_no_sandbox_pair()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(
            pty,
            new LaunchConfig { PermissionMode = PermissionMode.Bypass });

        pty.Last.Arguments.Should().Contain("--dangerously-bypass-approvals-and-sandbox");
        pty.Last.Arguments.Should().NotContain("--ask-for-approval");
    }

    // Codex has no classifier tier. It used to be *substituted* with `on-request` and an adjustment,
    // which only existed because the form offered every mode to every agent; now that
    // `CodexCapabilities` does not offer it, the honest outcome is a rejection carrying a message —
    // and never both, which is the never-silently-drop rule's whole shape.
    [Fact]
    public void Auto_is_rejected_because_Codex_does_not_offer_it()
    {
        var adapter = new CodexAdapter(new StubPtyHost(), new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<CodexAdapter>.Instance);

        var resolution = adapter.Resolve(new LaunchConfig { PermissionMode = PermissionMode.Auto });

        resolution.CanLaunch.Should().BeFalse();
        resolution.Rejections.Should().ContainSingle().Which.Should().Contain("Auto");
        resolution.Adjustments.Should().NotContain(
            adjustment => adjustment.Field == nameof(LaunchConfig.PermissionMode));
    }

    // The five it does offer all launch untouched — the other half of the same contract.
    [Fact]
    public void Every_mode_codex_offers_resolves_cleanly()
    {
        var adapter = new CodexAdapter(new StubPtyHost(), new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<CodexAdapter>.Instance);

        foreach (var mode in adapter.Capabilities.PermissionModes)
            adapter.Resolve(new LaunchConfig { PermissionMode = mode })
                .CanLaunch.Should().BeTrue($"Codex offers {mode}");
    }

    [Fact]
    public void Codex_offers_no_desktop_handoff()
    {
        var adapter = new CodexAdapter(new StubPtyHost(), new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<CodexAdapter>.Instance);

        adapter.Capabilities.DesktopHandoff.Should().BeFalse();
        adapter.DesktopHandoffUrl("any-session", "C:/repo").Should().BeNull();
    }

    [Fact]
    public async Task Resuming_passes_the_session_id_to_the_resume_subcommand()
    {
        var pty = new StubPtyHost();
        var adapter = new CodexAdapter(pty, new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<CodexAdapter>.Instance);

        await using var session = await adapter.ResumeAsync(new AgentResumeRequest(
            TaskId,
            "019ea722-d3f0-7563-b3cd-8ac7fb3f9a69",
            "C:/repo",
            "do the thing",
            null,
            new LaunchConfig(),
            TerminalSize.Default));

        pty.Last.Arguments.Should().StartWith(["resume", "019ea722-d3f0-7563-b3cd-8ac7fb3f9a69"]);
        session.SessionId.Should().Be("019ea722-d3f0-7563-b3cd-8ac7fb3f9a69");
    }

    // Per-model ladders are the reason capabilities changed shape; `ultra` exists only on terra.
    [Fact]
    public void An_effort_above_a_models_ladder_is_substituted_not_passed_through()
    {
        var adapter = new CodexAdapter(new StubPtyHost(), new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<CodexAdapter>.Instance);

        var resolution = adapter.Resolve(new LaunchConfig { Model = "gpt-5.5", Effort = "ultra" });

        resolution.CanLaunch.Should().BeTrue();
        resolution.Resolved.Effort.Should().NotBe("ultra");
        resolution.Adjustments.Should().Contain(
            adjustment => adjustment.Field == nameof(LaunchConfig.Effort));

        adapter.Resolve(new LaunchConfig { Model = "gpt-5.6-terra", Effort = "ultra" })
            .Resolved.Effort.Should().Be("ultra");
    }

    private static Task<IAgentSession> LaunchAsync(StubPtyHost pty, LaunchConfig? config = null)
        => new CodexAdapter(pty, new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<CodexAdapter>.Instance).LaunchAsync(new AgentLaunchRequest(
            TaskId,
            "ignored-by-codex",
            "C:/repo",
            "do the thing",
            config ?? new LaunchConfig(),
            TerminalSize.Default));
}
