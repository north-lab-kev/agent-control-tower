using System.Text.RegularExpressions;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

// S3 and S4 — the launch, and then the thing no other tier can show.
//
// S4 is the one worth the whole project. A card moves on screen because an *agent event* arrived, with
// nobody touching the browser: adapter → `SessionEventPump` → `BoardState` → `StateHasChanged` → SignalR
// → DOM. Every link in that chain is tested in isolation and the chain itself is not.
//
// The script is what makes it deterministic. `AwaitsKeystroke` parks the scripted session until the
// matching input actually arrives, so the test drives the *agent*, never a timer.
public sealed class LaunchTests : BrowserTest
{
    protected override void Arrange()
    {
        App.Cards.Add(Card(1, "Rename the widget", BoardColumn.Ready));

        App.Claude.Script = AgentScript.Start()
            .Activity("Read")
            .AwaitsKeystroke()
            .RequestsPermission("write to src/Widget.cs")
            .AwaitsKeystroke();
    }

    [Fact]
    public async Task Launching_moves_the_card_across_the_boundary()
    {
        await GoAsync();

        await Lane("Ready").Locator("div.strip").WaitForAsync();

        await Page.Locator("div.col button.launch").ClickAsync();

        await Assertions.Expect(Lane("Executing").Locator("div.strip")).ToHaveCountAsync(1);
        await Assertions.Expect(Lane("Ready").Locator("div.strip")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_launched_card_says_it_is_running()
    {
        await GoAsync();

        await Page.Locator("div.col button.launch").ClickAsync();

        await Assertions.Expect(Lane("Executing").Locator("div.strip span.badge")).ToHaveTextAsync("running");
    }

    [Fact]
    public async Task Launching_really_starts_a_session()
    {
        await GoAsync();

        await Page.Locator("div.col button.launch").ClickAsync();

        await Assertions.Expect(Lane("Executing").Locator("div.strip")).ToHaveCountAsync(1);

        var card = App.Board.In(BoardColumn.Executing).Single();

        card.SessionId.Should().NotBeNullOrEmpty();
        App.Registry.For(card.Id).Should().NotBeNull();
        App.Claude.Launches.Should().ContainSingle();
    }

    // S4. Nothing touches the browser between the launch and the move — the agent asks for permission, and
    // the board rearranges itself under a page that is just sitting there.
    [Fact]
    public async Task The_board_moves_itself_when_the_agent_asks_for_something()
    {
        await GoAsync();

        await Page.Locator("div.col button.launch").ClickAsync();
        await Assertions.Expect(Lane("Executing").Locator("div.strip")).ToHaveCountAsync(1);

        // The agent's turn to act, from the agent's side of the wire.
        await AnswerAgentAsync("y");

        await Assertions.Expect(Lane("Your turn").Locator("div.strip")).ToHaveCountAsync(1);
        await Assertions.Expect(Lane("Executing").Locator("div.strip")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_card_wanting_the_user_says_what_it_wants()
    {
        await GoAsync();

        await Page.Locator("div.col button.launch").ClickAsync();
        await Assertions.Expect(Lane("Executing").Locator("div.strip")).ToHaveCountAsync(1);

        await AnswerAgentAsync("y");

        var strip = Lane("Your turn").Locator("div.strip");

        await Assertions.Expect(strip.Locator("span.badge")).ToHaveTextAsync("needs permission");
        await Assertions.Expect(strip).ToHaveClassAsync(new Regex("attn"));
    }

    // The lane counts are rendered from the same pass, so a card that moved has to leave one count and
    // join another — a half-applied move would show it twice.
    [Fact]
    public async Task The_lane_counts_follow_the_card()
    {
        await GoAsync();

        await Assertions.Expect(Lane("Ready").Locator(".colcount")).ToHaveTextAsync("1");

        await Page.Locator("div.col button.launch").ClickAsync();

        await Assertions.Expect(Lane("Ready").Locator(".colcount")).ToHaveTextAsync("0");
        await Assertions.Expect(Lane("Executing").Locator(".colcount")).ToHaveTextAsync("1");
    }

    // Written into the live session rather than typed into the browser: this is the agent acting, and the
    // point of the test is that the *browser* did nothing.
    private async Task AnswerAgentAsync(string keystroke)
    {
        var card = App.Board.In(BoardColumn.Executing).Single();
        var session = App.Registry.For(card.Id);

        session.Should().NotBeNull();

        await session!.Terminal.WriteAsync(keystroke);
    }

    private ILocator Lane(string name)
        => Page.Locator("div.col").Filter(new LocatorFilterOptions
        {
            Has = Page.Locator(".colname", new PageLocatorOptions { HasTextString = name }),
        });
}
