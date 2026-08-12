using Act.Core.Model;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

// The rail's lineage links name cards whose face is this same component, so the router keeps the
// instance and only the route parameter changes — a click that a bUnit spec can only imitate with
// a parameter set. What this tier pins: the real click really swaps the page, both directions.
public sealed class LineageTests : BrowserTest
{
    protected override void Arrange()
    {
        var parent = Card(1, "The plan", BoardColumn.YourTurn);
        var child = Card(2, "Rename the widget", BoardColumn.YourTurn);

        child.ParentId = parent.Id;
        child.Origin = TaskOrigin.Spawned;
        parent.Children.Add(child.Id);

        App.Cards.Add(parent);
        App.Cards.Add(child);
    }

    [Fact]
    public async Task The_spawned_by_link_opens_the_parents_terminal()
    {
        var child = App.Board.All.Single(card => card.Number == 2);

        await GoAsync($"/card/{child.Id}/terminal");

        await Assertions.Expect(Page.Locator("header.bar span.title")).ToHaveTextAsync("Rename the widget");

        await Page.Locator("dl.facts a").ClickAsync();

        await Assertions.Expect(Page.Locator("header.bar span.title")).ToHaveTextAsync("The plan");
        await Assertions.Expect(Page.Locator("dl.facts a")).ToHaveTextAsync("#2 — Rename the widget");
    }

    [Fact]
    public async Task The_follow_up_link_opens_the_childs_terminal()
    {
        var parent = App.Board.All.Single(card => card.Number == 1);

        await GoAsync($"/card/{parent.Id}/terminal");

        await Assertions.Expect(Page.Locator("header.bar span.title")).ToHaveTextAsync("The plan");

        await Page.Locator("dl.facts a").ClickAsync();

        await Assertions.Expect(Page.Locator("header.bar span.title")).ToHaveTextAsync("Rename the widget");
        await Assertions.Expect(Page.Locator("dl.facts a")).ToHaveTextAsync("#1 — The plan");
    }
}
