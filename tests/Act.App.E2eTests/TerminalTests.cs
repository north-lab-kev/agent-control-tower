using Act.Core.Model;
using Act.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

// S5 and the two xterm guards.
//
// xterm needs a real window to paint — this is the one part of ACT that a headless component test can
// say nothing whatever about. What it can be held to: the agent's output reaches the screen, a keystroke
// reaches the agent, the backlog replays when the view comes back, and the two guards that were written
// because the unguarded versions misbehaved.
//
// The screen is read out of the DOM rather than screenshotted, for the reason recorded against the
// preview pane: a picture of a terminal proves nothing a string does not, and fails differently on every
// machine.
public sealed class TerminalTests : BrowserTest
{
    // Deliberately not something a keystroke could produce: if the assertion passes, the whole string
    // travelled together and it came off the clipboard.
    private const string Pasted = "git status --short";

    private Guid CardId => App.Board.All.Single().Id;

    protected override void Arrange()
    {
        App.Cards.Add(Card(1, "Rename the widget", BoardColumn.Ready));

        App.Claude.Script = AgentScript.Start()
            .Paints("Proceed with the rename? [y/n] ")
            .AwaitsKeystroke()
            .EndsTurn()
            .AwaitsKeystroke();
    }

    [Fact]
    public async Task What_the_agent_paints_reaches_the_screen()
    {
        await LaunchInTerminalAsync();

        await Assertions.Expect(Page.Locator("div.terminal .xterm-rows"))
            .ToContainTextAsync("Proceed with the rename?");
    }

    [Fact]
    public async Task A_keystroke_reaches_the_agent()
    {
        await LaunchInTerminalAsync();

        await TypeAsync("y");

        await Assertions.Expect(Page.Locator("div.terminal .xterm-rows")).ToContainTextAsync("Proceed");

        Typed().Should().Contain("y");
    }

    // The script parks on `AwaitsKeystroke` until the keystroke really arrives, so the turn ending is the
    // agent's own answer that it got it — no timer, and nothing to poll.
    [Fact]
    public async Task Answering_lets_the_agent_finish_its_turn()
    {
        await LaunchInTerminalAsync();

        await TypeAsync("y");

        await Assertions.Expect(Page.Locator("header.bar span.badge")).ToHaveTextAsync("to review");

        App.Board.Card(CardId)!.Column.Should().Be(BoardColumn.YourTurn);
    }

    // Ctrl+V is the one Ctrl key xterm has to be talked out of. It maps every Ctrl+letter to its control
    // character, so V reached the agent as SYN (0x16) with the keydown already cancelled, and the browser
    // never got as far as pasting. The context menu was the only route that ever worked, which is not
    // where anyone reaches for it.
    //
    // The barrier is the agent's own: the script parks on `AwaitsKeystroke` and ends its turn only once
    // something really arrived, so there is nothing to poll and no timer to tune.
    [Fact]
    public async Task Control_v_pastes_the_clipboard_into_the_agent()
    {
        await LaunchInTerminalAsync();

        await PutOnClipboardAsync(Pasted);

        await PressAsync("Control+v");

        await Assertions.Expect(Page.Locator("header.bar span.badge")).ToHaveTextAsync("to review");

        string.Concat(Typed()).Should().Contain(Pasted);
    }

    // The binding every other terminal uses, cancelled by xterm for the same reason. `isPasteKey`
    // deliberately does not test Shift, so both arrive here.
    [Fact]
    public async Task Control_shift_v_pastes_as_well()
    {
        await LaunchInTerminalAsync();

        await PutOnClipboardAsync(Pasted);

        await PressAsync("Control+Shift+v");

        await Assertions.Expect(Page.Locator("header.bar span.badge")).ToHaveTextAsync("to review");

        string.Concat(Typed()).Should().Contain(Pasted);
    }

    // The other half of the same decision, and the half that would go unnoticed: a key handed back to the
    // browser is a key the agent never sees again. If pasting were bought by widening `isPasteKey` to the
    // rest of the Ctrl range, no agent could be interrupted, suspended, or sent to the start of its line,
    // and nothing on screen would say why.
    [Theory]
    [MemberData(nameof(ControlRange))]
    public async Task The_agent_keeps_the_rest_of_the_control_range(string key, char expected)
    {
        await LaunchInTerminalAsync();

        await PressAsync(key);

        await Assertions.Expect(Page.Locator("header.bar span.badge")).ToHaveTextAsync("to review");

        string.Concat(Typed()).Should().Contain(expected.ToString());
    }

    // Written as code points rather than escapes so the control characters stay legible: ETX is the
    // interrupt, CAN begins Claude Code's `ctrl+x ctrl+e`, SOH is beginning-of-line, SUB suspends.
    public static TheoryData<string, char> ControlRange() => new()
    {
        { "Control+c", (char)3 },
        { "Control+x", (char)24 },
        { "Control+a", (char)1 },
        { "Control+z", (char)26 },
    };

    // Replay before subscribing takes effect for new chunks, so the screen looks the way it did when the
    // view was last open rather than blank until the agent next paints.
    [Fact]
    public async Task The_screen_comes_back_when_the_view_does()
    {
        await LaunchInTerminalAsync();

        await Assertions.Expect(Page.Locator("div.terminal .xterm-rows"))
            .ToContainTextAsync("Proceed with the rename?");

        // Away to another face of the same card, and back.
        await Page.Locator("nav.cardtabs a.tab").First.ClickAsync();
        await Assertions.Expect(Page.Locator("div.taskview")).ToBeVisibleAsync();

        await Page.Locator("nav.cardtabs a.tab").Nth(1).ClickAsync();

        await Assertions.Expect(Page.Locator("div.terminal .xterm-rows"))
            .ToContainTextAsync("Proceed with the rename?");
    }

    [Fact]
    public async Task The_session_survives_the_view_leaving()
    {
        await LaunchInTerminalAsync();

        var before = App.Registry.For(CardId);

        await Page.Locator("nav.cardtabs a.tab").First.ClickAsync();
        await Assertions.Expect(Page.Locator("div.taskview")).ToBeVisibleAsync();

        await Page.Locator("nav.cardtabs a.tab").Nth(1).ClickAsync();
        await Assertions.Expect(Page.Locator("div.terminal .xterm-screen")).ToBeVisibleAsync();

        App.Registry.For(CardId).Should().BeSameAs(before, "the registry owns the session, not the view");
    }

    // The first guard. `fit` is deliberately refused when the proposed geometry has not changed: calling it
    // unconditionally from the `ResizeObserver` is a feedback loop, and every resize makes the CLI repaint
    // its whole screen — the unguarded version flickered and grew without bound.
    [Fact]
    public async Task Resizing_reports_once_and_then_stops()
    {
        await LaunchInTerminalAsync();

        var before = Resizes();

        await Page.SetViewportSizeAsync(1000, 700);

        await Assertions.Expect(Page.Locator("div.terminal .xterm-screen")).ToBeVisibleAsync();

        // Long enough for a loop to give itself away: the refit is debounced at 60ms, so a runaway would
        // have added many by now.
        await Page.WaitForTimeoutAsync(1000);

        var settled = Resizes();

        settled.Should().BeGreaterThan(before, "the new geometry has to reach the agent");

        await Page.WaitForTimeoutAsync(1000);

        Resizes().Should().Be(settled, "nothing changed, so nothing more should be reported");
    }

    [Fact]
    public async Task A_resize_reports_the_geometry_the_agent_should_use()
    {
        await LaunchInTerminalAsync();

        await Page.SetViewportSizeAsync(1000, 700);
        await Page.WaitForTimeoutAsync(1000);

        var geometry = App.App.GetRequiredService<Act.App.Sessions.TerminalGeometry>().Last;

        geometry.Cols.Should().BeGreaterThan(0);
        geometry.Rows.Should().BeGreaterThan(0);
    }

    // The second guard. A restart resumes the session and replays *its* backlog; the dead session's output
    // left above it would read as one continuous screen when the two share nothing.
    [Fact]
    public async Task Restarting_clears_the_screen_before_it_rebinds()
    {
        await LaunchInTerminalAsync();

        await Assertions.Expect(Page.Locator("div.terminal .xterm-rows"))
            .ToContainTextAsync("Proceed with the rename?");

        App.Claude.Script = AgentScript.Start().Paints("a fresh terminal").AwaitsKeystroke();

        await Page.Locator("div.actions button")
            .Filter(new LocatorFilterOptions { HasTextString = "Restart terminal" })
            .ClickAsync();

        await Assertions.Expect(Page.Locator("div.terminal .xterm-rows")).ToContainTextAsync("a fresh terminal");

        var screen = await Page.Locator("div.terminal .xterm-rows").TextContentAsync();

        screen.Should().NotContain("Proceed with the rename?", "the dead session's screen is not this one's");
    }

    private int Resizes()
        => ((MockAgentSession)App.Registry.For(CardId)!).Received
            .Count(input => input.Kind is AgentInputKind.Resize);

    private IReadOnlyList<string> Typed()
        => [.. ((MockAgentSession)App.Registry.For(CardId)!).Received
            .Where(input => input.Kind is AgentInputKind.Write)
            .Select(input => input.Text!)];

    private async Task TypeAsync(string keys)
    {
        await Page.Locator("div.terminal .xterm-screen").ClickAsync();
        await Page.Keyboard.TypeAsync(keys);
    }

    // The click is what puts the focus on xterm's own textarea. Without it the key goes to the document
    // and the terminal never sees it, which looks exactly like the bug these specs are about.
    private async Task PressAsync(string key)
    {
        await Page.Locator("div.terminal .xterm-screen").ClickAsync();
        await Page.Keyboard.PressAsync(key);
    }

    // A real paste needs a real clipboard: the point is that the *browser's* native paste runs, so the
    // synthesised `paste` events `Clipboard` builds for `act-attach` would prove nothing here.
    private async Task PutOnClipboardAsync(string text)
    {
        await Page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);

        await Page.EvaluateAsync("text => navigator.clipboard.writeText(text)", text);
    }

    private async Task LaunchInTerminalAsync()
    {
        await GoAsync($"/card/{App.Board.All.Single().Id}/terminal");

        await Assertions.Expect(Page.Locator("div.terminal .xterm-screen")).ToBeVisibleAsync();

        await Page.Locator("div.actions button")
            .Filter(new LocatorFilterOptions { HasTextString = "Launch now" })
            .ClickAsync();

        await Assertions.Expect(Page.Locator("div.terminal .xterm-rows")).ToContainTextAsync("Proceed");
    }
}
