namespace Act.Core.Agents;

// Decides which interrupt keystrokes are worth reporting, from the keystrokes alone. It exists
// because of one measured asymmetry (`docs/findings/agent-interrupt.md`): an interrupt key ends the
// turn when the composer is empty **or** holds ordinary text, but an open picker — the slash-command
// list, the `@` file mention — eats the key instead, and the turn carries on. Reporting that one
// would put a working card up for review, which is worse than the bug this whole path fixes.
//
// So the keystroke ACT forwards is judged in the only context ACT legitimately has: the keys the user
// pressed before it. A picker trigger arms the doubt; the next interrupt key spends it, because that
// press is the one the picker consumed — and a second press, which is what really stops the turn, is
// reported. That is exactly the sequence measured for the slash menu.
//
// **This is a heuristic about somebody else's composer, and it is deliberately biased.** When it is
// wrong it stays *silent* rather than lying: a missed interrupt leaves the card claiming `running`
// until the user's next keypress, which is the pre-existing behaviour, while a false one moves a card
// that is still working. The named ways it goes silent are in the findings file, along with the
// alternative — dropping the looser key — that this replaced.
public sealed class TurnInterruptWatch(TurnInterruptProfile profile)
{
    private readonly Lock gate = new();

    // Whether a picker may be open. Nothing here tracks *what* the composer holds: a caret model over
    // someone else's line editor is the brittleness this design exists to avoid, and one bit is all
    // the difference between the measured cases needs.
    private bool pickerArmed;

    public bool ReportsInterrupt(string data)
    {
        lock (gate)
        {
            if (profile.Keys.Contains(data))
            {
                var swallowed = pickerArmed;

                pickerArmed = false;

                return !swallowed;
            }

            // Submitting closes whatever was open, and the composer starts again from empty.
            if (data is "\r" or "\n")
            {
                pickerArmed = false;

                return false;
            }

            foreach (var character in data)
            {
                if (profile.PickerTriggers.Contains(character))
                    pickerArmed = true;
            }

            return false;
        }
    }
}
