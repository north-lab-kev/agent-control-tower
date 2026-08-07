using Act.Core.Model;
using Act.Core.Spawning;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.Core.Tests;

// What a follow-up inherits and what it may override — the whole of `create_followup`'s argument
// contract, minus the transport. The `same` cases matter more than the explicit ones: an agent that
// supplies only a title and a prompt is the common call, and everything it did not say has to come
// from the parent rather than from a default nobody chose.
public class FollowUpResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly StubCapabilityCatalog catalog = new();

    private readonly StubWorkingDirectories directories = new();

    [Fact]
    public void A_minimal_call_inherits_everything_from_the_parent()
    {
        var child = Resolved(Parent(), Ask());

        child.Title.Should().Be("Rename the widget");
        child.InitialPrompt.Should().Be("Do the thing.");
        child.AgentType.Should().Be(AgentType.ClaudeCode);
        child.WorkingDir.Should().Be("C:/repo");
        child.LaunchConfig.Model.Should().Be(StubCapabilityCatalog.ClaudeModel);
        child.LaunchConfig.Effort.Should().Be("high");
        child.LaunchConfig.PermissionMode.Should().Be(PermissionMode.AcceptEdits);
    }

    // The lineage and the landing column are ACT's, never the agent's — there is no argument for any
    // of these, which is what stops an agent choosing its own leash length.
    [Fact]
    public void A_follow_up_lands_in_ready_marked_as_the_agents_own()
    {
        var parent = Parent();
        var child = Resolved(parent, Ask());

        child.Column.Should().Be(BoardColumn.Ready);
        child.Origin.Should().Be(TaskOrigin.Spawned);
        child.SpawnAuthor.Should().Be(SpawnAuthor.Agent);
        child.ParentId.Should().Be(parent.Id);
        child.CreatedAt.Should().Be(Now);
        child.Number.Should().Be(0);
    }

    [Theory]
    [InlineData("claude", AgentType.ClaudeCode)]
    [InlineData("codex", AgentType.Codex)]
    [InlineData("Codex", AgentType.Codex)]
    [InlineData("claude-code", AgentType.ClaudeCode)]
    [InlineData(null, AgentType.ClaudeCode)]
    [InlineData("same", AgentType.ClaudeCode)]
    public void The_agent_may_be_chosen_or_inherited(string? asked, AgentType expected)
    {
        var parent = Parent();
        parent.LaunchConfig.PermissionMode = PermissionMode.Default;

        Resolved(parent, Ask() with { Agent = asked }).AgentType.Should().Be(expected);
    }

    // The case the whole `same`-across-agents rule exists for: `opus` is a Claude slug, and carrying
    // it onto a Codex card would turn every cross-agent spawn into a rejection for a value the agent
    // never chose. Across agents, `same` means the new agent's own default.
    [Fact]
    public void Handing_work_to_the_other_agent_does_not_carry_the_parents_model_across()
    {
        var parent = Parent();
        parent.LaunchConfig.Model = "opus";
        parent.LaunchConfig.PermissionMode = PermissionMode.Default;

        var child = Resolved(parent, Ask() with { Agent = "codex" });

        child.AgentType.Should().Be(AgentType.Codex);
        child.LaunchConfig.Model.Should().Be(StubCapabilityCatalog.CodexModel);
    }

    // Permission is the exception, and deliberately: the mode is a normalized value designed to keep
    // meaning when a card is retargeted, so it crosses where a model slug cannot.
    [Fact]
    public void The_permission_mode_does_cross_agents_because_it_is_normalized()
    {
        var parent = Parent();
        parent.LaunchConfig.PermissionMode = PermissionMode.Plan;

        Resolved(parent, Ask() with { Agent = "codex" })
            .LaunchConfig.PermissionMode.Should().Be(PermissionMode.Plan);
    }

    // Accepted rather than overlooked — see the spec. The child still lands in Ready, so an
    // escalation is on the board before anything runs.
    [Fact]
    public void A_follow_up_may_be_more_permissive_than_its_parent()
    {
        var parent = Parent();
        parent.LaunchConfig.PermissionMode = PermissionMode.Plan;

        Resolved(parent, Ask() with { Permission = "bypass" })
            .LaunchConfig.PermissionMode.Should().Be(PermissionMode.Bypass);
    }

    [Fact]
    public void An_inherited_permission_the_target_agent_lacks_is_refused_rather_than_dropped()
    {
        var parent = Parent();
        parent.LaunchConfig.PermissionMode = PermissionMode.AcceptEdits;

        var resolution = FollowUpResolver.Resolve(
            parent, Ask() with { Agent = "codex" }, catalog, directories, [parent], Now);

        resolution.CanCreate.Should().BeFalse();
        resolution.Rejections.Should().ContainSingle().Which.Should().Contain("AcceptEdits");
    }

    // No `same`: a parent's schedule is a trigger that already fired.
    [Theory]
    [InlineData(null, TaskSchedule.Manual)]
    [InlineData("manual", TaskSchedule.Manual)]
    [InlineData("now", TaskSchedule.Now)]
    [InlineData("next_window", TaskSchedule.NextWindow)]
    public void The_schedule_defaults_to_manual_and_is_never_inherited(string? asked, TaskSchedule expected)
    {
        var parent = Parent();
        parent.Schedule = TaskSchedule.Now;

        Resolved(parent, Ask() with { Schedule = asked }).Schedule.Should().Be(expected);
    }

    [Fact]
    public void Same_is_not_a_schedule()
        => Refusals(Parent(), Ask() with { Schedule = "same" })
            .Should().ContainSingle().Which.Should().Contain("manual, now, next_window");

    [Fact]
    public void The_git_action_is_inherited_with_its_draft_flag()
    {
        var parent = Parent();
        parent.AutoGit = new AutoGitOptions { Action = GitAction.PullRequest, Draft = true };

        var child = Resolved(parent, Ask());

        child.AutoGit!.Action.Should().Be(GitAction.PullRequest);
        child.AutoGit.Draft.Should().BeTrue();
    }

    [Fact]
    public void The_git_action_can_be_turned_off()
    {
        var parent = Parent();
        parent.AutoGit = new AutoGitOptions { Action = GitAction.Commit };

        Resolved(parent, Ask() with { AutoGit = "none" }).AutoGit.Should().BeNull();
    }

    [Fact]
    public void A_working_directory_may_be_given_and_otherwise_comes_from_the_parent()
    {
        Resolved(Parent(), Ask() with { WorkingDir = "C:/other" }).WorkingDir.Should().Be("C:/other");
        Resolved(Parent(), Ask() with { WorkingDir = "   " }).WorkingDir.Should().Be("C:/repo");
    }

    // The same rule the task form enforces before a card may be saved: a relative path would resolve
    // against whatever directory ACT happens to be running in, so the MCP path must not store what
    // the form would refuse.
    [Fact]
    public void A_relative_working_directory_is_refused_like_the_form_refuses_it()
        => Refusals(Parent(), Ask() with { WorkingDir = "src" })
            .Should().ContainSingle().Which.Should().Contain("absolute");

    // Any card, not only a sibling — which is exactly why `list_tasks` exists.
    [Fact]
    public void Dependencies_may_name_any_card_the_board_knows()
    {
        var unrelated = Card(Guid.NewGuid());

        Resolved(Parent(), Ask() with { DependsOn = [unrelated.Id.ToString()] }, unrelated)
            .DependsOn.Should().ContainSingle().Which.Should().Be(unrelated.Id);
    }

    [Fact]
    public void An_id_that_is_not_an_id_is_refused_rather_than_skipped()
        => Refusals(Parent(), Ask() with { DependsOn = ["the second one"] })
            .Should().ContainSingle().Which.Should().Contain("list_tasks");

    // A well-formed GUID that names nothing would be accepted by the parse, gate nothing at launch
    // (`DependencyGate` treats a missing prerequisite as satisfied), and the ordering the agent asked
    // for would silently never happen. The refusal at creation is the only moment it can be caught.
    [Fact]
    public void An_id_that_names_no_card_is_refused_rather_than_stored()
        => Refusals(Parent(), Ask() with { DependsOn = [Guid.NewGuid().ToString()] })
            .Should().ContainSingle().Which.Should().Contain("does not name a task");

    [Theory]
    [InlineData("", "Do the thing.")]
    [InlineData("   ", "Do the thing.")]
    [InlineData("Rename the widget", "")]
    public void A_follow_up_needs_both_a_title_and_a_prompt(string title, string prompt)
        => Refusals(Parent(), new FollowUpRequest(title, prompt)).Should().ContainSingle();

    // Every bad argument in one call, so a confused agent gets one round trip rather than four.
    [Fact]
    public void Every_bad_argument_is_reported_at_once()
        => Refusals(
                Parent(),
                Ask() with { Agent = "gemini", Permission = "yolo", Schedule = "tuesday", AutoGit = "rebase" })
            .Should().HaveCount(4);

    [Fact]
    public void An_unknown_model_names_the_agent_it_is_not_available_for()
        => Refusals(Parent(), Ask() with { Model = "gpt-5.3" })
            .Should().ContainSingle().Which.Should().Contain("ClaudeCode");

    // Advisory everywhere it exists, so it is substituted and recorded rather than blocking — the
    // same asymmetry `LaunchConfigResolver` already draws, inherited rather than re-decided here.
    [Fact]
    public void An_unavailable_effort_is_substituted_and_reported()
    {
        var parent = Parent();
        var resolution = FollowUpResolver.Resolve(
            parent, Ask() with { Effort = "ludicrous" }, catalog, directories, [parent], Now);

        resolution.CanCreate.Should().BeTrue();
        resolution.Adjustments.Should().ContainSingle().Which.Field.Should().Be(nameof(LaunchConfig.Effort));
    }

    [Fact]
    public void A_refused_call_produces_no_card()
    {
        var parent = Parent();

        FollowUpResolver.Resolve(parent, Ask() with { Agent = "gemini" }, catalog, directories, [parent], Now)
            .Card.Should().BeNull();
    }

    // The other half of every knob: overriding actually overrides. Without these the suite would only
    // prove that inheritance works, and a resolver that ignored explicit values would pass it.
    [Fact]
    public void An_explicit_model_and_effort_are_honoured()
    {
        var child = Resolved(Parent(), Ask() with { Model = "opus", Effort = "low" });

        child.LaunchConfig.Model.Should().Be("opus");
        child.LaunchConfig.Effort.Should().Be("low");
    }

    [Theory]
    [InlineData("default", PermissionMode.Default)]
    [InlineData("plan", PermissionMode.Plan)]
    [InlineData("acceptEdits", PermissionMode.AcceptEdits)]
    [InlineData("accept_edits", PermissionMode.AcceptEdits)]
    [InlineData("auto", PermissionMode.Auto)]
    [InlineData("dontAsk", PermissionMode.DontAsk)]
    [InlineData("dont_ask", PermissionMode.DontAsk)]
    [InlineData("bypassPermissions", PermissionMode.Bypass)]
    public void Every_permission_the_description_offers_is_accepted(string asked, PermissionMode expected)
        => Resolved(Parent(), Ask() with { Permission = asked })
            .LaunchConfig.PermissionMode.Should().Be(expected);

    [Theory]
    [InlineData("commit", GitAction.Commit)]
    [InlineData("push", GitAction.Push)]
    [InlineData("pr", GitAction.PullRequest)]
    [InlineData("pull_request", GitAction.PullRequest)]
    public void Every_git_action_the_description_offers_is_accepted(string asked, GitAction expected)
        => Resolved(Parent(), Ask() with { AutoGit = asked }).AutoGit!.Action.Should().Be(expected);

    // Draft is not an argument, so an explicitly chosen action starts undrafted rather than quietly
    // carrying a preference the agent never restated.
    [Fact]
    public void An_explicitly_chosen_pull_request_is_not_a_draft()
    {
        var parent = Parent();
        parent.AutoGit = new AutoGitOptions { Action = GitAction.PullRequest, Draft = true };

        Resolved(parent, Ask() with { AutoGit = "pr" }).AutoGit!.Draft.Should().BeFalse();
    }

    // Effort is a per-model ladder, so it cannot cross agents any more than a model slug can.
    [Fact]
    public void Handing_work_to_the_other_agent_resets_the_effort_too()
    {
        var parent = Parent();
        parent.LaunchConfig.Effort = "high";
        parent.LaunchConfig.PermissionMode = PermissionMode.Default;

        Resolved(parent, Ask() with { Agent = "codex" }).LaunchConfig.Effort.Should().Be("low");
    }

    // The sentinel comes from a model, so it will arrive spelled however the model felt like spelling it.
    [Theory]
    [InlineData("same")]
    [InlineData("Same")]
    [InlineData("SAME")]
    [InlineData(" same ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void The_inherit_sentinel_is_forgiving(string? asked)
        => Resolved(Parent(), Ask() with { Agent = asked, Model = asked })
            .LaunchConfig.Model.Should().Be(StubCapabilityCatalog.ClaudeModel);

    [Fact]
    public void Whitespace_around_the_text_fields_is_taken_off()
    {
        var child = Resolved(
            Parent(),
            new FollowUpRequest("  Padded title  ", "  Padded prompt  ", WorkingDir: "  C:/other  "));

        child.Title.Should().Be("Padded title");
        child.InitialPrompt.Should().Be("Padded prompt");
        child.WorkingDir.Should().Be("C:/other");
    }

    [Fact]
    public void No_dependencies_is_an_empty_list_rather_than_null()
    {
        Resolved(Parent(), Ask()).DependsOn.Should().BeEmpty();
        Resolved(Parent(), Ask() with { DependsOn = [] }).DependsOn.Should().BeEmpty();
    }

    // Nothing about a follow-up may waive the folder guard — it is the user's, and an agent queueing a
    // card that runs beside its own parent in the same tree is the collision the guard exists for.
    [Fact]
    public void A_follow_up_never_exempts_itself_from_the_folder_guard()
    {
        var parent = Parent();
        parent.AllowConcurrentWorkingDir = true;

        Resolved(parent, Ask()).AllowConcurrentWorkingDir.Should().BeFalse();
    }

    // The child is a task, not a copy of the session that asked for it.
    [Fact]
    public void A_follow_up_inherits_nothing_that_belongs_to_the_parents_run()
    {
        var parent = Parent();
        parent.SessionId = "019fbb34-secret";
        parent.Attachments.Add(new TaskAttachment { FileName = "spec.pdf" });
        parent.Metrics = new CardMetrics { TurnCount = 7 };

        var child = Resolved(parent, Ask());

        child.SessionId.Should().BeNull();
        child.Attachments.Should().BeEmpty();
        child.Metrics.Should().BeNull();
        child.Transitions.Should().BeEmpty();
        child.LaunchedAt.Should().BeNull();
    }

    private Card Resolved(Card parent, FollowUpRequest request, params Card[] others)
    {
        var resolution = FollowUpResolver.Resolve(parent, request, catalog, directories, [parent, .. others], Now);

        resolution.Rejections.Should().BeEmpty();

        return resolution.Card!;
    }

    private IReadOnlyList<string> Refusals(Card parent, FollowUpRequest request)
        => FollowUpResolver.Resolve(parent, request, catalog, directories, [parent], Now).Rejections;

    private static FollowUpRequest Ask() => new("Rename the widget", "Do the thing.");

    private static Card Card(Guid id) => new()
    {
        Id = id,
        Number = 1040,
        Title = "Another task",
        AgentType = AgentType.ClaudeCode,
        WorkingDir = "C:/elsewhere",
        Column = BoardColumn.Ready,
    };

    private static Card Parent() => new()
    {
        Id = Guid.NewGuid(),
        Number = 1039,
        Title = "The parent",
        AgentType = AgentType.ClaudeCode,
        WorkingDir = "C:/repo",
        Column = BoardColumn.Executing,
        LaunchConfig = new LaunchConfig
        {
            Model = StubCapabilityCatalog.ClaudeModel,
            Effort = "high",
            PermissionMode = PermissionMode.AcceptEdits,
        },
    };
}
