using Act.App.Components.Pages;
using Act.App.Components.Shared;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// The task form minus the parts that belong to a single task: no attachments, no specific datetime, and a
// Save rather than settings' apply-as-you-type, because `/template/new` has nothing to apply to until the
// user commits it. The cascade rules are the same as the task form's and are the reason this page is not
// simply a settings section — two copies of that rule is the thing that can drift, so both are pinned.
public class TemplateViewTests : ComponentTest
{
    [Fact]
    public void A_new_template_opens_on_a_blank_form()
    {
        var cut = Show();

        cut.Find("header.bar .title").TextContent.Should().Be("New template");
        Name(cut).Should().BeEmpty();
    }

    [Fact]
    public void A_new_template_starts_on_an_agent_that_is_switched_on()
    {
        Settings.SetDefaults(new AgentDefaults { Agent = AgentType.ClaudeCode, Enabled = false });

        RadzenDom.Selected(Show(), "Agent").Should().Be("codex");
    }

    [Fact]
    public void An_existing_template_opens_on_its_own_values()
    {
        var template = Saved("Nightly sweep");

        var cut = Show(template.Id);

        cut.Find("header.bar .title").TextContent.Should().Be("Nightly sweep");
        Name(cut).Should().Be("Nightly sweep");
        cut.Find("div.dirrow input").GetAttribute("value").Should().Be("/dev/nightly");
    }

    // Fixed when the page loads rather than read off the live form: the heading is where you are, and clearing
    // the name box to retype it must not blank the strip you are standing on.
    [Fact]
    public void The_heading_does_not_follow_the_name_box()
    {
        var cut = Show(Saved("Nightly sweep").Id);

        cut.FindAll("input.rz-textbox")[0].Change(string.Empty);

        cut.Find("header.bar .title").TextContent.Should().Be("Nightly sweep");
    }

    // A route can be reached with a stale id — a bookmark, a back button after a delete.
    [Fact]
    public void An_id_that_is_not_there_says_so_and_offers_nothing_to_edit()
    {
        var cut = Show(Guid.NewGuid());

        cut.Find("div.templateview.empty").TextContent.Should().NotBeNullOrWhiteSpace();
        cut.FindAll("form").Should().BeEmpty();
    }

    // Its name is not the user's to change, and a title or prompt on the template the bare New task button
    // starts from would make every task begin as a copy of the last.
    [Fact]
    public void The_default_carries_no_name_no_title_and_no_prompt()
    {
        var cut = Show(Settings.DefaultTemplate.Id);

        cut.FindAll("textarea").Should().BeEmpty();
        cut.FindAll("input.rz-textbox").Should().ContainSingle("only the working directory");
    }

    [Fact]
    public void A_named_template_carries_all_three()
    {
        var cut = Show(Saved("Nightly sweep").Id);

        cut.FindAll("textarea").Should().ContainSingle();
        cut.FindAll("input.rz-textbox").Should().HaveCount(3, "name, task title and working directory");
    }

    // No title field for the *template's* own task title on the default, and never a specific datetime: that
    // is a moment rather than a habit.
    [Fact]
    public void A_template_is_never_scheduled_for_a_specific_moment()
    {
        RadzenDom.Options(Show(), "Schedule").Should().NotContain("specific date & time");
    }

    [Fact]
    public void There_is_nothing_to_attach_to_a_template()
    {
        var cut = Show();

        cut.FindAll("div.dropzone").Should().BeEmpty();
        cut.FindAll("div.chips").Should().BeEmpty();
    }

    // The same rule the task form follows, restated here because this is a second implementation of it.
    [Fact]
    public void Switching_agent_drops_a_model_the_new_one_does_not_offer()
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Model", MockAgentAdapter.FastModel);
        RadzenDom.Choose(cut, "Agent", "codex");

        RadzenDom.Selected(cut, "Model").Should().Be("agent default");
    }

    [Fact]
    public void Switching_agent_resets_a_permission_mode_the_new_one_refuses()
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Permission mode", "accept edits");
        RadzenDom.Choose(cut, "Agent", "codex");

        RadzenDom.Selected(cut, "Permission mode").Should().Be("default");
    }

    [Fact]
    public void Switching_model_drops_an_effort_the_new_model_does_not_offer()
    {
        var cut = Show();

        RadzenDom.Choose(cut, "Model", MockAgentAdapter.DeepModel);
        RadzenDom.Choose(cut, "Reasoning effort", "max");
        RadzenDom.Choose(cut, "Model", MockAgentAdapter.FastModel);

        RadzenDom.Selected(cut, "Reasoning effort").Should().Be("agent default");
    }

    [Fact]
    public void A_retired_model_stays_in_the_list_of_the_template_that_holds_it()
    {
        var template = Saved("Nightly sweep");
        template.Model = "mock-retired";

        Settings.SaveTemplate(template);

        RadzenDom.Options(Show(template.Id), "Model").Should().Contain("mock-retired");
    }

    [Fact]
    public void The_draft_switch_shows_only_for_a_pull_request()
    {
        var cut = Show();

        RadzenDom.HasRow(cut, "Draft").Should().BeFalse();

        RadzenDom.Choose(cut, "Git when done", "commit + push + PR");

        RadzenDom.HasRow(cut, "Draft").Should().BeTrue();
    }

    [Fact]
    public void The_folder_exemption_shows_only_while_the_guard_is_on()
    {
        RadzenDom.HasRow(Show(), "Run even if the folder is busy").Should().BeTrue();

        Settings.SetPreventConcurrentWorkingDir(false);

        RadzenDom.HasRow(Show(), "Run even if the folder is busy").Should().BeFalse();
    }

    [Fact]
    public async Task Saving_persists_it_and_returns_to_the_list()
    {
        var cut = Show();

        cut.FindAll("input.rz-textbox")[0].Change("Nightly sweep");
        cut.Find("div.dirrow input").Change("/dev/nightly");

        await cut.Find("form").SubmitAsync();

        Settings.Templates.Should().HaveCount(2);
        Settings.Templates.Single(template => !template.IsDefault).WorkingDir.Should().Be("/dev/nightly");
        Route.Should().Be("templates");
    }

    [Fact]
    public async Task A_template_with_no_name_is_refused()
    {
        var cut = Show();

        cut.Find("div.dirrow input").Change("/dev/nightly");

        await cut.Find("form").SubmitAsync();

        Settings.Templates.Should().ContainSingle("only the default");
        Route.Should().BeEmpty();
    }

    [Fact]
    public void Cancelling_returns_to_the_list_without_saving()
    {
        var cut = Show();

        cut.FindAll("input.rz-textbox")[0].Change("Never saved");
        cut.FindAll("div.foot button")[0].Click();

        Settings.Templates.Should().ContainSingle();
        Route.Should().Be("templates");
    }

    // The picker is part of this page rather than a popup of its own, so Escape has to close it here.
    [Fact]
    public async Task Escape_closes_an_open_picker_before_it_leaves()
    {
        Directories.With("/dev/nightly", "src");

        var cut = Show(Saved("Nightly sweep").Id);

        cut.Find("div.dirrow button").Click();
        cut.FindAll("div.picker").Should().ContainSingle();

        await cut.InvokeAsync(() => cut.FindComponent<EscapeKey>().Instance.Escaped());

        cut.FindAll("div.picker").Should().BeEmpty();
        Route.Should().BeEmpty();

        await cut.InvokeAsync(() => cut.FindComponent<EscapeKey>().Instance.Escaped());

        Route.Should().Be("templates");
    }

    [Fact]
    public void A_picked_folder_replaces_whatever_was_typed()
    {
        Directories.With("/dev/nightly", "src").With("/dev/nightly/src", "Act.Core");

        var cut = Show(Saved("Nightly sweep").Id);

        cut.Find("div.dirrow button").Click();
        cut.Find("button.entry").Click();
        cut.Find("div.pickeractions button:last-child").Click();

        cut.Find("div.dirrow input").GetAttribute("value").Should().Be("/dev/nightly/src");
    }

    private static string Name(IRenderedComponent<TemplateView> cut)
        => cut.FindAll("input.rz-textbox")[0].GetAttribute("value") ?? string.Empty;

    private TaskTemplate Saved(string name)
    {
        var template = new TaskTemplate
        {
            Id = Guid.NewGuid(),
            Name = name,
            WorkingDir = "/dev/nightly",
            Prompt = "Sweep the thing",
        };

        Settings.SaveTemplate(template);

        return template;
    }

    private IRenderedComponent<TemplateView> Show(Guid? templateId = null)
        => Render<TemplateView>(p =>
        {
            if (templateId is { } id)
                p.Add(c => c.TemplateId, id);
        });
}
