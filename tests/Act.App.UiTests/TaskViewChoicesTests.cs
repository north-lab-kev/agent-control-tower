using Act.App.Components.Pages;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// Model, effort and permission mode are all facts about the *chosen agent*, and the ladders differ per
// model as well as per agent. So switching one has to drop what the new one cannot honour — otherwise the
// card stores a combination the launch rejects, and the user finds out at spawn. What keeps these lists
// honest is that they also have to hold a value the agent no longer offers: an editing card must never
// show an empty dropdown for something it is still carrying.
//
// The two mock adapters differ on purpose — see `ComponentTest.NarrowerCapabilities`.
public class TaskViewChoicesTests : ComponentTest
{
    [Fact]
    public void The_agent_list_is_the_ones_that_are_switched_on()
    {
        RadzenDom.Options(Show(), "Agent").Should().Equal("claude", "codex");
    }

    // An agent turned off after a card was made must not leave that card showing an empty dropdown for a
    // value it is still holding — the same rule a retired model follows.
    [Fact]
    public async Task A_card_keeps_its_own_agent_in_the_list_even_after_it_is_switched_off()
    {
        var card = TaskViewTests.Card(BoardColumn.Ready);
        card.AgentType = AgentType.Codex;

        await BoardWith(card);

        Settings.SetDefaults(new AgentDefaults { Agent = AgentType.Codex, Enabled = false });

        var cut = Show(card.Id);

        RadzenDom.Options(cut, "Agent").Should().Contain("codex");
        RadzenDom.Selected(cut, "Agent").Should().Be("codex");
    }

    [Fact]
    public void An_agent_that_is_switched_off_is_not_offered_on_a_new_task()
    {
        Settings.SetDefaults(new AgentDefaults { Agent = AgentType.Codex, Enabled = false });

        RadzenDom.Options(Show(), "Agent").Should().Equal("claude");
    }

    // Whatever the chosen agent declares, in its order. Neither the form nor the list knows why one
    // offers two models and the other one.
    [Fact]
    public void The_model_list_is_the_chosen_agent_own()
    {
        var cut = Show();

        RadzenDom.Options(cut, "Model").Should().Equal(MockAgentAdapter.FastModel, MockAgentAdapter.DeepModel);

        RadzenDom.Choose(cut, "Agent", "codex");

        RadzenDom.Options(cut, "Model").Should().Equal(MockAgentAdapter.DeepModel);
    }

    [Fact]
    public void Switching_agent_drops_a_model_the_new_one_does_not_offer()
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Model", MockAgentAdapter.FastModel);
        RadzenDom.Selected(cut, "Model").Should().Be(MockAgentAdapter.FastModel);

        RadzenDom.Choose(cut, "Agent", "codex");

        RadzenDom.Selected(cut, "Model").Should().Be("agent default");
    }

    [Fact]
    public void Switching_agent_keeps_a_model_the_new_one_does_offer()
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Model", MockAgentAdapter.DeepModel);
        RadzenDom.Choose(cut, "Agent", "codex");

        RadzenDom.Selected(cut, "Model").Should().Be(MockAgentAdapter.DeepModel);
    }

    // Unlike a model, a permission mode has no "agent default" to fall back to — the field is required
    // and every agent honours `Default`.
    [Fact]
    public void Switching_agent_resets_a_permission_mode_the_new_one_refuses()
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Permission mode", "accept edits");
        RadzenDom.Selected(cut, "Permission mode").Should().Be("accept edits");

        RadzenDom.Choose(cut, "Agent", "codex");

        RadzenDom.Selected(cut, "Permission mode").Should().Be("default");
        RadzenDom.Options(cut, "Permission mode").Should().Equal("default", "plan");
    }

    [Fact]
    public void Switching_agent_keeps_a_permission_mode_the_new_one_honours()
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Permission mode", "plan");
        RadzenDom.Choose(cut, "Agent", "codex");

        RadzenDom.Selected(cut, "Permission mode").Should().Be("plan");
    }

    // The ladders differ per model, not only per agent, so the same rule has to run when the model
    // changes underneath a chosen effort.
    [Fact]
    public void Switching_model_drops_an_effort_the_new_model_does_not_offer()
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Model", MockAgentAdapter.DeepModel);
        RadzenDom.Choose(cut, "Reasoning effort", "max");
        RadzenDom.Selected(cut, "Reasoning effort").Should().Be("max");

        RadzenDom.Choose(cut, "Model", MockAgentAdapter.FastModel);

        RadzenDom.Selected(cut, "Reasoning effort").Should().Be("agent default");
    }

    [Fact]
    public void Switching_agent_drops_an_effort_with_the_model_that_carried_it()
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Model", MockAgentAdapter.DeepModel);
        RadzenDom.Choose(cut, "Reasoning effort", "max");
        RadzenDom.Choose(cut, "Agent", "codex");

        RadzenDom.Selected(cut, "Reasoning effort").Should().Be("max", "the narrower agent still offers it");
    }

    // "Agent default" is not "no model", so the ladder shown is the one the launch will actually accept —
    // offering nothing here made the field unusable until a model was named.
    [Fact]
    public void Efforts_are_offered_against_the_agent_default_while_the_model_box_is_blank()
    {
        var cut = Show();

        RadzenDom.Selected(cut, "Model").Should().Be("agent default");
        RadzenDom.Options(cut, "Reasoning effort").Should().Equal("low", "high");
    }

    // A stored value the agent no longer offers stays visible, or editing a card would show an empty
    // dropdown for something it is still carrying.
    [Fact]
    public async Task A_retired_model_stays_in_the_list_of_the_card_that_holds_it()
    {
        var card = TaskViewTests.Card(BoardColumn.Ready);
        card.LaunchConfig = new LaunchConfig { Model = "mock-retired" };

        await BoardWith(card);

        var cut = Show(card.Id);

        RadzenDom.Options(cut, "Model").Should().Contain("mock-retired");
        RadzenDom.Selected(cut, "Model").Should().Be("mock-retired");
    }

    [Fact]
    public async Task A_retired_effort_stays_in_the_list_of_the_card_that_holds_it()
    {
        var card = TaskViewTests.Card(BoardColumn.Ready);
        card.LaunchConfig = new LaunchConfig { Model = MockAgentAdapter.FastModel, Effort = "extreme" };

        await BoardWith(card);

        RadzenDom.Options(Show(card.Id), "Reasoning effort").Should().Contain("extreme");
    }

    [Fact]
    public void The_date_field_shows_only_for_a_schedule_that_wants_one()
    {
        var cut = Show();

        RadzenDom.HasRow(cut, "Date").Should().BeFalse();

        RadzenDom.Choose(cut, "Schedule", "specific date & time");

        RadzenDom.HasRow(cut, "Date").Should().BeTrue();

        RadzenDom.Choose(cut, "Schedule", "now");

        RadzenDom.HasRow(cut, "Date").Should().BeFalse();
    }

    // A warning, never a block: the combination is legal and the user may have a reason. What it must not
    // be is a surprise at 3am.
    [Fact]
    public void An_unattended_task_that_will_stop_at_a_prompt_says_so()
    {
        var cut = Show();

        cut.FindAll("div.rz-alert.warn").Should().BeEmpty("a manual task is watched by definition");

        RadzenDom.Choose(cut, "Schedule", "now");

        cut.Find("div.rz-alert.warn").TextContent.Should().Contain("ACT never answers a prompt");
    }

    // `acceptEdits` counts as prompting: it covers edits and still stops at the first `Bash` call it wants
    // approval for.
    [Theory]
    [InlineData("default", true)]
    [InlineData("accept edits", true)]
    [InlineData("auto", false)]
    [InlineData("bypass", false)]
    public void Whether_it_warns_depends_on_the_mode_asking_or_not(string mode, bool warns)
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Schedule", "now");
        RadzenDom.Choose(cut, "Permission mode", mode);

        cut.FindAll("div.rz-alert.warn").Should().HaveCount(warns ? 1 : 0);
    }

    // PR-only: a draft is a property of a pull request, so choosing another action has nothing to be a
    // draft of.
    [Fact]
    public void The_draft_switch_shows_only_for_a_pull_request()
    {
        var cut = Show();

        RadzenDom.HasRow(cut, "Draft").Should().BeFalse();

        RadzenDom.Choose(cut, "Git when done", "commit + push + PR");

        RadzenDom.HasRow(cut, "Draft").Should().BeTrue();

        RadzenDom.Choose(cut, "Git when done", "commit");

        RadzenDom.HasRow(cut, "Draft").Should().BeFalse();
    }

    // Only while the guard is on: an exemption from a rule nobody is enforcing is a switch that reads as
    // if it did something.
    [Fact]
    public void The_folder_exemption_shows_only_while_the_guard_is_on()
    {
        RadzenDom.HasRow(Show(), "Run even if the folder is busy").Should().BeTrue();

        Settings.SetPreventConcurrentWorkingDir(false);

        RadzenDom.HasRow(Show(), "Run even if the folder is busy").Should().BeFalse();
    }

    // The hint under the permission box is the mode's own, so a user reading it is reading about what they
    // just chose.
    [Fact]
    public void The_permission_hint_follows_the_mode()
    {
        var cut = Show();

        var forDefault = RadzenDom.Row(cut, "Permission mode").QuerySelector(".hint")!.TextContent;

        RadzenDom.Choose(cut, "Permission mode", "plan");

        RadzenDom.Row(cut, "Permission mode").QuerySelector(".hint")!.TextContent
            .Should().NotBeNullOrWhiteSpace().And.NotBe(forDefault);
    }

    private IRenderedComponent<TaskView> Show(Guid? cardId = null)
        => Render<TaskView>(p =>
        {
            if (cardId is { } id)
                p.Add(c => c.CardId, id);
        });
}
