using Act.Core.Agents;
using AwesomeAssertions;

namespace Act.Core.Tests;

// The measured asymmetry this class exists for, one test per row of the table in
// `docs/findings/agent-interrupt.md`. Measured against `claude-code 2.1.235`: an interrupt key stops
// the turn from an empty composer and from one holding ordinary text, but an **open picker eats it**
// and the turn carries on — for `Esc` and Ctrl+C alike. Reporting that press would put a working card
// up for review, which is the bug the `Esc` support first shipped with.
public class TurnInterruptWatchTests
{
    private static readonly TurnInterruptProfile ClaudeCode = new(
        [TurnInterruptKeys.CtrlC, TurnInterruptKeys.Escape],
        ['/', '@']);

    [Theory]
    [InlineData(TurnInterruptKeys.Escape)]
    [InlineData(TurnInterruptKeys.CtrlC)]
    public void An_interrupt_key_from_an_empty_composer_is_reported(string key)
        => Watch().ReportsInterrupt(key).Should().BeTrue();

    // Measured: ordinary text does **not** stop the key reaching the turn, so this must be reported.
    // The first `Esc` interrupted with `hello there` sitting in the composer.
    [Theory]
    [InlineData(TurnInterruptKeys.Escape)]
    [InlineData(TurnInterruptKeys.CtrlC)]
    public void An_interrupt_key_after_ordinary_typing_is_reported(string key)
    {
        var watch = Watch();

        Type(watch, "hello there");

        watch.ReportsInterrupt(key).Should().BeTrue();
    }

    // The user's bug, and the measured sequence behind the fix: `/` opens the command list, the first
    // key closes it and the agent keeps working, the second is the one that stops the turn.
    [Theory]
    [InlineData(TurnInterruptKeys.Escape)]
    [InlineData(TurnInterruptKeys.CtrlC)]
    public void The_key_an_open_picker_eats_is_not_reported_and_the_next_one_is(string key)
    {
        var watch = Watch();

        Type(watch, "/");

        watch.ReportsInterrupt(key).Should().BeFalse("an open picker consumes the key, and the turn carries on");
        watch.ReportsInterrupt(key).Should().BeTrue("the picker is closed now, so this press reaches the turn");
    }

    // `@` opens the file mention picker mid-sentence rather than only at the start, which is why the
    // trigger is looked for anywhere in what was typed rather than at the front of a composer model.
    [Fact]
    public void A_mention_picker_opened_mid_sentence_is_noticed()
    {
        var watch = Watch();

        Type(watch, "look at @");

        watch.ReportsInterrupt(TurnInterruptKeys.Escape).Should().BeFalse();
    }

    // Submitting closes whatever was open and empties the composer, so the doubt must not outlive it —
    // otherwise every interrupt after a slash command went unreported for the rest of the session.
    [Fact]
    public void Submitting_clears_the_doubt()
    {
        var watch = Watch();

        Type(watch, "/init");
        watch.ReportsInterrupt("\r").Should().BeFalse("Enter is not an interrupt");

        watch.ReportsInterrupt(TurnInterruptKeys.Escape).Should().BeTrue();
    }

    [Fact]
    public void A_newline_submit_clears_it_too()
    {
        var watch = Watch();

        Type(watch, "@file");
        watch.ReportsInterrupt("\n");

        watch.ReportsInterrupt(TurnInterruptKeys.Escape).Should().BeTrue();
    }

    // One trigger arms one press, not a session. Two pickers in a row is still one key each.
    [Fact]
    public void The_doubt_is_spent_by_one_press()
    {
        var watch = Watch();

        Type(watch, "/");
        watch.ReportsInterrupt(TurnInterruptKeys.Escape).Should().BeFalse();
        watch.ReportsInterrupt(TurnInterruptKeys.Escape).Should().BeTrue();

        Type(watch, "@");
        watch.ReportsInterrupt(TurnInterruptKeys.CtrlC).Should().BeFalse();
        watch.ReportsInterrupt(TurnInterruptKeys.CtrlC).Should().BeTrue();
    }

    // An arrow key is the escape byte plus more, in one chunk — the whole-chunk match is what keeps it
    // from ending a turn, and it must not arm anything either.
    [Theory]
    [InlineData("[A")]
    [InlineData("[B")]
    [InlineData("OP")]
    public void An_escape_sequence_is_neither_an_interrupt_nor_a_trigger(string rest)
    {
        var watch = Watch();

        watch.ReportsInterrupt(TurnInterruptKeys.Escape + rest).Should().BeFalse();
        watch.ReportsInterrupt(TurnInterruptKeys.Escape).Should().BeTrue();
    }

    // A path pasted or dropped into the terminal carries a slash, so it arms the doubt and costs one
    // press. Named here rather than worked around: the cost is a card that stays `running` a keystroke
    // longer, which is the pre-existing behaviour, and the alternative is guessing at what a paste means.
    [Fact]
    public void A_pasted_path_costs_one_press()
    {
        var watch = Watch();

        Type(watch, "C:/repo/notes.md ");

        watch.ReportsInterrupt(TurnInterruptKeys.Escape).Should().BeFalse();
        watch.ReportsInterrupt(TurnInterruptKeys.Escape).Should().BeTrue();
    }

    // The Codex shape: no keys, no triggers, nothing reported however the user types.
    [Fact]
    public void An_agent_with_no_measured_interrupt_reports_nothing()
    {
        var watch = new TurnInterruptWatch(TurnInterruptProfile.None);

        watch.ReportsInterrupt(TurnInterruptKeys.CtrlC).Should().BeFalse();
        watch.ReportsInterrupt(TurnInterruptKeys.Escape).Should().BeFalse();
    }

    private static TurnInterruptWatch Watch() => new(ClaudeCode);

    // One call per character, which is what xterm does: `onData` fires per keypress.
    private static void Type(TurnInterruptWatch watch, string text)
    {
        foreach (var character in text)
            watch.ReportsInterrupt(character.ToString()).Should().BeFalse($"'{character}' is not an interrupt");
    }
}
