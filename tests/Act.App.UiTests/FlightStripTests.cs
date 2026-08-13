using Act.App.Components.Board;
using Act.Core.Model;
using Act.Core.Scheduling;
using AwesomeAssertions;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Act.App.UiTests;

// `StripFaceTests` pins what a strip *says*; this pins that the component actually says it, and that
// the gestures on it land where the board expects. Those are the two halves a pure test cannot
// reach: whether a computed class makes it onto the element the CSS paints, and whether clicking the
// launch button starts the card without also opening it — the button sits inside a strip that is
// itself a click target, so `stopPropagation` is the whole reason the board does not navigate away
// from a card the user just launched.
public class FlightStripTests : ComponentTest
{
    [Fact]
    public void The_face_reaches_the_element_the_css_paints()
    {
        var card = Card(BoardColumn.YourTurn);
        card.Badge = Badge.NeedsPermission;

        var cut = Strip(card);

        cut.Find("div.strip").ClassName.Should().Be("strip r-wait attn liftable");
        cut.Find(".badge").TextContent.Should().Be("needs permission");
    }

    // A hold borrows the badge slot a Ready card leaves empty, and the sentence explaining it hangs
    // off the wrapping element rather than the chip — worth a render test, because a `title` on the
    // wrong element is invisible in a unit test and silent in the browser.
    [Fact]
    public void A_hold_explains_itself_on_the_element_that_carries_the_tooltip()
    {
        var blocker = Card(BoardColumn.Executing);
        blocker.Number = 7;
        blocker.Title = "Already here";

        var cut = Strip(Card(BoardColumn.Ready), p =>
            p.Add(c => c.Hold, new ReadyHold(LaunchHold.WorkingDir, Blocker: blocker)));

        var chip = cut.Find(".b-hold");

        chip.TextContent.Should().Contain("7");
        chip.ParentElement!.GetAttribute("title").Should().Contain("Already here");
    }

    [Theory]
    [InlineData(BoardColumn.Ready, "true")]
    [InlineData(BoardColumn.Executing, "false")]
    public void Only_a_column_a_card_may_leave_by_hand_lifts(BoardColumn column, string expected)
    {
        Strip(Card(column)).Find("div.strip").GetAttribute("draggable").Should().Be(expected);
    }

    [Fact]
    public void Clicking_a_strip_opens_its_card()
    {
        var card = Card(BoardColumn.Executing);
        Card? opened = null;

        var cut = Strip(card, p => p.Add(c => c.OnOpen, c => opened = c));

        cut.Find("div.strip").Click();

        opened.Should().BeSameAs(card);
    }

    // The strip is a `role="button"`, so the keyboard has to open it too — and only on the two keys
    // that mean "activate".
    [Theory]
    [InlineData("Enter", true)]
    [InlineData(" ", true)]
    [InlineData("a", false)]
    public void The_keyboard_opens_a_strip_the_way_a_button_opens(string key, bool expected)
    {
        var opened = false;

        var cut = Strip(Card(BoardColumn.Executing), p => p.Add(c => c.OnOpen, _ => opened = true));

        cut.Find("div.strip").KeyDown(new KeyboardEventArgs { Key = key });

        opened.Should().Be(expected);
    }

    [Fact]
    public void Launching_a_card_does_not_also_open_it()
    {
        var card = Card(BoardColumn.Ready);
        Card? launched = null;
        var opened = false;

        var cut = Strip(card, p =>
        {
            p.Add(c => c.OnLaunch, c => launched = c);
            p.Add(c => c.OnOpen, _ => opened = true);
        });

        cut.Find("button.launch").Click();

        launched.Should().BeSameAs(card);
        opened.Should().BeFalse("the launch click is stopped at the button, not left to bubble to the strip");
    }

    // Same reason as the launch button, and it matters more here: a delete click that bubbled would
    // delete the card *and* navigate to the card it just deleted.
    [Fact]
    public void Deleting_a_card_does_not_also_open_it()
    {
        var card = Card(BoardColumn.Executing);
        Card? deleted = null;
        var opened = false;

        var cut = Strip(card, p =>
        {
            p.Add(c => c.OnDelete, c => deleted = c);
            p.Add(c => c.OnOpen, _ => opened = true);
        });

        cut.Find("button.del").Click();

        deleted.Should().BeSameAs(card);
        opened.Should().BeFalse("the delete click is stopped at the button, not left to bubble to the strip");
    }

    // Every column, because tidying the board is not a thing you can only do to cards that never ran —
    // what changes by column is whether it stops to ask, and that is the board's decision.
    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    [InlineData(BoardColumn.Completed)]
    public void Both_densities_offer_the_delete_on_every_column(BoardColumn column)
    {
        Strip(Card(column)).FindAll("button.del").Should().ContainSingle();
        Strip(Card(column), density: BoardDensity.Compact).FindAll("button.del").Should().ContainSingle();
    }

    // A delete already under way must not be startable twice: the second click would ask about the
    // follow-ups of a card that is already gone.
    [Fact]
    public void A_delete_in_flight_disables_its_own_button()
    {
        var cut = Strip(Card(BoardColumn.Executing), p => p.Add(c => c.IsDeleting, true));

        cut.Find("button.del").HasAttribute("disabled").Should().BeTrue();
    }

    // Density is what the strip itself decides — `StripFace` computes the identity line either way.
    // Compact drops it for the bare number, and keeps the launch button as an icon, because a compact
    // board is the one you start work from.
    [Fact]
    public void A_compact_strip_gives_up_the_identity_line_but_not_the_launch_button()
    {
        var card = Card(BoardColumn.Ready);
        card.ObservedModel = "claude-opus-5";

        Strip(card).Find(".cid").TextContent.Should().Be("#1042 · claude · claude-opus-5");

        var compact = Strip(card, density: BoardDensity.Compact);

        compact.Find(".cid").TextContent.Should().Be("#1042");
        compact.FindAll("button.launch").Should().ContainSingle();
    }

    // The lineage rail on the detailed strip: a spawned card names its parent, and a parent counts
    // what it left behind. `StripFaceTests` pins the wording; this pins that both actually render,
    // and on the elements the CSS paints.
    [Fact]
    public void A_detailed_strip_marks_lineage_in_both_directions()
    {
        var parent = Card(BoardColumn.Executing);
        parent.Number = 7;

        var card = Card(BoardColumn.Ready);
        card.Origin = TaskOrigin.Spawned;
        card.ParentId = parent.Id;
        card.Children.Add(Guid.NewGuid());
        card.Children.Add(Guid.NewGuid());

        var cut = Strip(card, p => p.Add(c => c.Parent, parent));

        var lineage = cut.FindAll(".lin");

        lineage.Should().HaveCount(2);
        lineage[0].TextContent.Should().Contain("7");
        lineage[1].TextContent.Should().Contain("2");
    }

    // Compact still says a card was spawned — one glyph beside the path, where the children counter
    // deliberately does not fit.
    [Fact]
    public void A_compact_strip_keeps_the_spawned_marker_and_gives_up_the_counter()
    {
        var parent = Card(BoardColumn.Executing);
        parent.Number = 7;

        var card = Card(BoardColumn.Ready);
        card.Origin = TaskOrigin.Spawned;
        card.ParentId = parent.Id;
        card.Children.Add(Guid.NewGuid());

        var cut = Strip(card, p => p.Add(c => c.Parent, parent), BoardDensity.Compact);

        cut.FindAll(".lin.clin").Should().ContainSingle().Which.TextContent.Should().Contain("7");
        cut.FindAll(".lin").Should().ContainSingle("compact trades the children counter away");
    }

    // The spinner says a better title is on its way; the placeholder stays legible beside it, so a
    // pending strip is still a searchable one. Both densities carry it — a title being written is
    // worth the same pixel wherever the title is read.
    [Theory]
    [InlineData(BoardDensity.Detailed)]
    [InlineData(BoardDensity.Compact)]
    public async Task A_pending_title_spins_beside_the_placeholder(BoardDensity density)
    {
        Claude.QueryHeld = new TaskCompletionSource();

        var card = Card(BoardColumn.Preparing);
        card.InitialPrompt = "Rename the widget everywhere";

        await BoardWith(card);
        Backfill.Start(card);

        var cut = Strip(card, density: density);

        cut.Find(".ttlspin").GetAttribute("title").Should().Be("Generating a title…");
        cut.Find(".ttl").TextContent.Should().Be("Rename the widget");

        Claude.QueryHeld.SetResult();

        await Backfill.Idle;

        cut.Render();

        cut.FindAll(".ttlspin").Should().BeEmpty();
    }

    [Fact]
    public void A_settled_title_shows_no_spinner()
    {
        Strip(Card(BoardColumn.Preparing)).FindAll(".ttlspin").Should().BeEmpty();
    }

    // What an untitled card looks like once title writing is switched off: the prompt's opening words in
    // the title's place, with no spinner, because nothing is on its way. Both densities, because a card
    // with no name of its own is exactly the one a compact board still has to be able to find.
    [Theory]
    [InlineData(BoardDensity.Detailed)]
    [InlineData(BoardDensity.Compact)]
    public void An_untitled_card_is_named_by_its_prompt(BoardDensity density)
    {
        var card = Card(BoardColumn.Preparing);
        card.Title = string.Empty;
        card.InitialPrompt = "Rename the widget everywhere";

        var cut = Strip(card, density: density);

        cut.Find(".ttl").TextContent.Should().Be("Rename the widget everywhere");
        cut.Find("div.strip").GetAttribute("title").Should().Be("Rename the widget everywhere");
        cut.FindAll(".ttlspin").Should().BeEmpty();
    }

    private IRenderedComponent<FlightStrip> Strip(
        Card card,
        Action<ComponentParameterCollectionBuilder<FlightStrip>>? parameters = null,
        BoardDensity density = BoardDensity.Detailed)
        => Render<FlightStrip>(p =>
        {
            p.Add(c => c.Card, card);
            p.Add(c => c.Density, density);
            parameters?.Invoke(p);
        });

    private static Card Card(BoardColumn column) => new()
    {
        Number = 1042,
        Title = "Rename the widget",
        Column = column,
        AgentType = AgentType.ClaudeCode,
        WorkingDir = "/dev/act",
    };
}
