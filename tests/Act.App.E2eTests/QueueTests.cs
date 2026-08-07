using System.Text.RegularExpressions;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

// The unattended path — the one users trust most and watch least. Nothing in the browser starts these
// cards: `QueueRunner` does, on its own, from a pass nobody asked for.
//
// It is worth a round trip because the runner is wired to three separate signals (a board write, a
// settings change, its own backstop timer) and lives behind `BackgroundWork`'s single-flight gate. Every
// piece is unit-tested; that it actually runs inside a booted app is not.
public sealed class QueueTests : BrowserTest
{
    protected override void Arrange()
    {
        var queued = Card(1, "Nightly sweep", BoardColumn.Ready);
        queued.Schedule = TaskSchedule.Now;

        App.Cards.Add(queued);

        App.Claude.Script = AgentScript.Start().Activity("Read").AwaitsKeystroke();
    }

    [Fact]
    public async Task A_task_scheduled_to_run_now_is_running_without_anyone_launching_it()
    {
        await GoAsync();

        await Assertions.Expect(Lane("Executing").Locator("div.strip")).ToHaveCountAsync(1);
        await Assertions.Expect(Lane("Ready").Locator("div.strip")).ToHaveCountAsync(0);

        App.Claude.Launches.Should().ContainSingle("the queue started it, not a click");
    }

    [Fact]
    public async Task The_card_the_queue_started_has_a_real_session()
    {
        await GoAsync();

        await Assertions.Expect(Lane("Executing").Locator("div.strip")).ToHaveCountAsync(1);

        var card = App.Board.In(BoardColumn.Executing).Single();

        card.SessionId.Should().NotBeNullOrEmpty();
        App.Registry.For(card.Id).Should().NotBeNull();
    }

    private ILocator Lane(string name)
        => Page.Locator("div.col").Filter(new LocatorFilterOptions
        {
            Has = Page.Locator(".colname", new PageLocatorOptions { HasTextString = name }),
        });
}

// The other half of the same promise, and the one worth more: the pause switch has to actually stop the
// runner. A switch that only greyed the chip would let ACT start work at 3am that the user thought they
// had stopped.
public sealed class PausedQueueTests : BrowserTest
{
    protected override void Arrange()
    {
        var queued = Card(1, "Nightly sweep", BoardColumn.Ready);
        queued.Schedule = TaskSchedule.Now;

        App.Cards.Add(queued);

        App.Claude.Script = AgentScript.Start().Activity("Read").AwaitsKeystroke();

        App.Paused = true;
    }

    [Fact]
    public async Task Nothing_starts_while_automatic_execution_is_paused()
    {
        await GoAsync();

        await Assertions.Expect(Page.Locator("div.viewctl button").First).ToHaveClassAsync(new Regex("paused"));

        // Long enough for the runner's own pass to have happened and decided against it.
        await Page.WaitForTimeoutAsync(1000);

        await Assertions.Expect(Page.Locator("div.col div.strip")).ToHaveCountAsync(1);

        App.Board.In(BoardColumn.Ready).Should().ContainSingle();
        App.Claude.Launches.Should().BeEmpty();
    }

    // The strip says why it is sitting still, read from the runner's own last evaluation rather than
    // computed again — so the chip and the decision cannot disagree.
    [Fact]
    public async Task The_card_says_why_it_is_waiting()
    {
        await GoAsync();

        await Assertions.Expect(Page.Locator("div.col div.strip")).ToContainTextAsync("paused");
    }

    // And releasing the switch lets it go, with nobody touching the card itself.
    [Fact]
    public async Task Unpausing_lets_the_queue_start_it()
    {
        await GoAsync();

        await Assertions.Expect(Page.Locator("div.col div.strip")).ToContainTextAsync("paused");

        await Page.Locator("div.viewctl button").First.ClickAsync();

        await Assertions.Expect(Page.Locator("div.col div.strip span.badge")).ToHaveTextAsync("running");

        App.Claude.Launches.Should().ContainSingle();
    }
}
