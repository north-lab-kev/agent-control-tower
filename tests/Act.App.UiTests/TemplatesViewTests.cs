using Act.App.Components.Pages;
using Act.Core.Model;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// The saved starting points a task can be created from. One rule carries the page: the default has no delete
// at all rather than a disabled one, because a board whose New task button has nothing to start from is not a
// state to offer.
public class TemplatesViewTests : ComponentTest
{
    [Fact]
    public void A_fresh_install_lists_the_default_and_nothing_else()
    {
        var cut = Show();

        cut.FindAll("div.list div.row").Should().ContainSingle();
    }

    [Fact]
    public void Every_template_gets_a_row()
    {
        Settings.SaveTemplate(Template("Nightly sweep"));
        Settings.SaveTemplate(Template("Quick fix"));

        Show().FindAll("div.list div.row").Should().HaveCount(3);
    }

    // Sorted by the name the user reads, because this list is read to *find* a template and insertion order is
    // an order only the person who created them knows.
    [Fact]
    public void The_rows_are_sorted_by_the_name_that_is_shown()
    {
        Settings.SaveTemplate(Template("Zebra"));
        Settings.SaveTemplate(Template("Alpha"));

        var names = Show().FindAll("div.list div.row .ttl").Select(row => row.TextContent).ToList();

        names.Should().BeInAscendingOrder(StringComparer.CurrentCultureIgnoreCase);
        names.Should().Contain("Alpha").And.Contain("Zebra");
    }

    // Enough to recognise a template without opening it, which is the whole job of the row: what it runs,
    // where, and how much of the prompt it already carries.
    [Fact]
    public void A_row_says_what_it_runs_and_where()
    {
        var template = Template("Nightly sweep");
        template.WorkingDir = "/dev/nightly";
        template.Agent = AgentType.Codex;
        template.Prompt = "Sweep the thing";

        Settings.SaveTemplate(template);

        var meta = Show().FindAll("div.list div.row")
            .Single(row => row.QuerySelector(".ttl")!.TextContent == "Nightly sweep")
            .QuerySelector(".meta")!.TextContent;

        meta.Should().Contain("codex").And.Contain("/dev/nightly");
    }

    [Fact]
    public void A_template_with_no_prompt_admits_to_it()
    {
        Settings.SaveTemplate(Template("Empty"));

        var meta = Show().FindAll("div.list div.row")
            .Single(row => row.QuerySelector(".ttl")!.TextContent == "Empty")
            .QuerySelector(".meta")!.TextContent;

        meta.Should().Contain("no prompt");
    }

    // The default has no delete at all rather than a disabled one.
    [Fact]
    public void The_default_cannot_be_deleted()
    {
        Settings.SaveTemplate(Template("Nightly sweep"));

        var rows = Show().FindAll("div.list div.row");

        var forDefault = rows.Single(row => row.QuerySelector(".ttl")!.TextContent != "Nightly sweep");
        var forNamed = rows.Single(row => row.QuerySelector(".ttl")!.TextContent == "Nightly sweep");

        forDefault.QuerySelectorAll("button").Should().ContainSingle("only edit");
        forNamed.QuerySelectorAll("button").Should().HaveCount(2, "edit and delete");
    }

    [Fact]
    public void Opening_a_template_goes_to_its_own_route()
    {
        var template = Template("Nightly sweep");

        Settings.SaveTemplate(template);

        var cut = Show();

        cut.FindAll("div.list div.row")
            .Single(row => row.QuerySelector(".ttl")!.TextContent == "Nightly sweep")
            .QuerySelector("button")!.Click();

        Route.Should().Be($"template/{template.Id}");
    }

    [Fact]
    public void A_new_template_is_asked_for_from_the_header()
    {
        Show().Find("div.add button").Click();

        Route.Should().Be("template/new");
    }

    [Fact]
    public async Task Deleting_asks_first_and_names_the_template()
    {
        Settings.SaveTemplate(Template("Nightly sweep"));

        var cut = Show();

        Delete(cut);

        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());
        Settings.Templates.Should().HaveCount(2, "nothing goes until the question is answered");
    }

    [Fact]
    public async Task Confirming_deletes_it()
    {
        Settings.SaveTemplate(Template("Nightly sweep"));

        var cut = Show();

        Delete(cut);
        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, true);

        cut.WaitForAssertion(() => Settings.Templates.Should().ContainSingle());
    }

    [Fact]
    public async Task Declining_keeps_it()
    {
        Settings.SaveTemplate(Template("Nightly sweep"));

        var cut = Show();

        Delete(cut);
        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, false);

        Settings.Templates.Should().HaveCount(2);
    }

    [Fact]
    public void Leaving_goes_back_to_the_board()
    {
        Show().Find("header.bar button").Click();

        Route.Should().BeEmpty();
    }

    private static void Delete(IRenderedComponent<TemplatesView> cut)
        => cut.FindAll("div.list div.row")
            .Single(row => row.QuerySelector(".ttl")!.TextContent == "Nightly sweep")
            .QuerySelectorAll("button")[1].Click();

    private IRenderedComponent<TemplatesView> Show() => Render<TemplatesView>();

    private static TaskTemplate Template(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        WorkingDir = "/dev/act",
    };
}
