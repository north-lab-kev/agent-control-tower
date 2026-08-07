using Act.Core.Model;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

// S1 — the ten-second sanity check, and the first thing to run when anything here goes wrong.
//
// What it is really testing is the handover: Blazor Server sends prerendered HTML and then swaps in an
// interactive circuit over SignalR. If that breaks, the page still *looks* right and every one of the
// 607 component tests still passes — this is the only test in the build that notices.
public sealed class CircuitTests : BrowserTest
{
    protected override void Arrange()
        => App.Cards.Add(Card(1, "Rename the widget", BoardColumn.Ready));

    [Fact]
    public async Task The_board_is_served()
    {
        await GoAsync();

        await Assertions.Expect(Page.Locator("div.col")).ToHaveCountAsync(5);
        await Assertions.Expect(Page.Locator("div.col div.strip")).ToHaveCountAsync(1);
    }

    // A prerendered page renders identical markup and answers no events at all. Clicking something and
    // watching the *server's* state change is the only honest proof the circuit is live.
    [Fact]
    public async Task The_circuit_is_live_and_writes_reach_the_store()
    {
        await GoAsync();

        App.Settings.Density.Should().Be(BoardDensity.Detailed);

        await Page.Locator("div.viewctl button").Nth(1).ClickAsync();

        await Assertions.Expect(Page.Locator("div.board.compact")).ToBeVisibleAsync();

        App.Settings.Density.Should().Be(BoardDensity.Compact);
    }

    // Through LiteDB and back. The component tests run on a fake store, so this is the first time the
    // real mapper and the real file are involved in anything.
    [Fact]
    public async Task A_setting_survives_a_reload()
    {
        await GoAsync();

        await Page.Locator("div.viewctl button").Nth(1).ClickAsync();
        await Assertions.Expect(Page.Locator("div.board.compact")).ToBeVisibleAsync();

        await GoAsync();

        await Assertions.Expect(Page.Locator("div.board.compact")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_shell_reaches_its_three_destinations()
    {
        await GoAsync();

        await Page.Locator("div.actions button").Nth(2).ClickAsync();
        await Assertions.Expect(Page.Locator("div.settingsview")).ToBeVisibleAsync();

        await GoAsync();

        await Page.Locator("div.actions button").Nth(1).ClickAsync();
        await Assertions.Expect(Page.Locator("div.archive")).ToBeVisibleAsync();

        await GoAsync();

        await Page.Locator("div.actions button").First.ClickAsync();
        await Assertions.Expect(Page.Locator("div.templates")).ToBeVisibleAsync();
    }

    // A url typed or bookmarked, rather than arrived at by clicking. Routing is a fake in every other
    // tier.
    [Fact]
    public async Task A_card_url_resolves_on_its_own()
    {
        await GoAsync();

        var id = App.Board.In(BoardColumn.Ready).Single().Id;

        await GoAsync($"/card/{id}/edit");

        await Assertions.Expect(Page.Locator("header.bar span.title")).ToHaveTextAsync("Rename the widget");
    }
}
