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

    // Codex has no classifier tier, so `CodexCapabilities` does not offer `Auto` and the honest
    // outcome is a rejection carrying a message — substituted or rejected, never both, which is the
    // never-silently-drop rule's whole shape.
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

    // `--image` is the whole reason the two adapters differ over attachments: the CLI reads the file
    // itself, so the picture lands on turn one instead of behind a tool call the sandbox could refuse.
    //
    // The `=` form is load-bearing and is a measured trap, not a style choice: `-i` is variadic, so
    // `-i <path>` followed by the positional prompt swallows the prompt into the image list and Codex
    // then blocks waiting on stdin for one.
    [Fact]
    public async Task An_image_rides_the_native_flag_and_is_left_out_of_the_prompt()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty, attachments: Attached(
            new AgentAttachment(@"C:\act\a\shot.png", IsImage: true)));

        pty.Last.Arguments.Should().Contain(@"--image=C:\act\a\shot.png");
        pty.Last.Arguments.Should().NotContain("-i");
        pty.Last.Arguments[^1].Should().Be("do the thing");
    }

    [Fact]
    public async Task Several_images_each_get_their_own_bound_flag()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty, attachments: Attached(
            new AgentAttachment(@"C:\act\a\one.png", IsImage: true),
            new AgentAttachment(@"C:\act\a\two.jpg", IsImage: true)));

        pty.Last.Arguments.Should().Contain(@"--image=C:\act\a\one.png")
            .And.Contain(@"--image=C:\act\a\two.jpg");
        pty.Last.Arguments[^1].Should().Be("do the thing");
    }

    // Everything `-i` does not cover is named instead. No grant goes with it: Codex reads outside
    // `--cd` under both sandboxes, and its own `--add-dir` makes a directory *writable*, which is not
    // what a reference file wants.
    [Fact]
    public async Task A_file_that_is_not_an_image_is_named_in_the_prompt_without_a_grant()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty, attachments: Attached(
            new AgentAttachment(@"C:\act\a\trace.log", IsImage: false)));

        pty.Last.Arguments.Should().NotContain(argument => argument.StartsWith("--image", StringComparison.Ordinal));
        pty.Last.Arguments.Should().NotContain("--add-dir");
        pty.Last.Arguments[^1].Should().Contain(@"C:\act\a\trace.log");
    }

    // A resumed session already carries its opening files in the transcript it reopened, so `-i`
    // would attach every image a second time.
    [Fact]
    public async Task A_resume_re_attaches_nothing()
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
            TerminalSize.Default,
            Attached(new AgentAttachment(@"C:\act\a\shot.png", IsImage: true))));

        pty.Last.Arguments.Should().NotContain(argument => argument.Contains(@"C:\act\a\shot.png", StringComparison.Ordinal));
    }

    // The task form lists the models in the order the catalog declares them, so the catalog's
    // order is the dropdown's order — strongest first, weakest last.
    [Fact]
    public void The_models_are_listed_strongest_to_weakest()
        => CodexCapabilities.Current.Models.Select(model => model.Slug)
            .Should().Equal("gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5", "gpt-5.4-mini");

    private static AgentAttachments Attached(params AgentAttachment[] files)
        => new(@"C:\act\a", files);

    private static Task<IAgentSession> LaunchAsync(
        StubPtyHost pty,
        LaunchConfig? config = null,
        AgentAttachments? attachments = null)
        => new CodexAdapter(pty, new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<CodexAdapter>.Instance).LaunchAsync(new AgentLaunchRequest(
            TaskId,
            "ignored-by-codex",
            "C:/repo",
            "do the thing",
            config ?? new LaunchConfig(),
            TerminalSize.Default,
            attachments));
}
