using Act.App.Components.Shared;
using AwesomeAssertions;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Act.App.UiTests;

// Asked on the way to saving a template, so the only two answers that matter are "this name" and
// "never mind" — and the caller distinguishes them by `null`, which is also what the X and the overlay
// produce. The Enter guard is the part worth pinning: a keystroke that closed a blank dialog would
// save a template with no name at all.
public class TemplateNameDialogTests : ComponentTest
{
    private const string Save = "div.actions button:last-child";

    private const string Cancel = "div.actions button:first-child";

    [Fact]
    public void The_suggestion_fills_the_box()
    {
        Dialog("Rename the widget").Host.WaitForElement("div.asktemplate input")
            .GetAttribute("value").Should().Be("Rename the widget");
    }

    [Fact]
    public void A_blank_suggestion_cannot_be_saved_yet()
    {
        Dialog(string.Empty).Host.WaitForElement(Save).HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task Saving_closes_with_the_name()
    {
        (await Dialog("Rename the widget").ClosedWith(Save)).Should().Be("Rename the widget");
    }

    [Fact]
    public async Task Enter_saves_a_named_template()
    {
        var run = Dialog("Rename the widget");

        run.Host.WaitForElement("div.asktemplate input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        ((object?)await run.Result).Should().Be("Rename the widget");
    }

    [Fact]
    public void Enter_on_a_blank_name_does_nothing()
    {
        var run = Dialog(string.Empty);

        run.Host.WaitForElement("div.asktemplate input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        run.Result.IsCompleted.Should().BeFalse();
    }

    // The trim is what makes " Nightly " and "Nightly" the same template rather than two.
    [Fact]
    public async Task The_name_is_trimmed_on_the_way_out()
    {
        (await Dialog("  Nightly sweep  ").ClosedWith(Save)).Should().Be("Nightly sweep");
    }

    [Fact]
    public void Whitespace_is_not_a_name()
    {
        Dialog("   ").Host.WaitForElement(Save).HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task Cancelling_closes_with_nothing()
    {
        (await Dialog("Rename the widget").ClosedWith(Cancel)).Should().BeNull();
    }

    private DialogRun<TemplateNameDialog> Dialog(string suggested)
        => OpenDialog<TemplateNameDialog>((nameof(TemplateNameDialog.Suggested), suggested));
}
