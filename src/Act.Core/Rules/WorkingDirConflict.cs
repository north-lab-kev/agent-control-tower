using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.Core.Rules;

// One folder is one working tree, and two agents editing it at once is a collision no amount of
// prompt care avoids: the second one reads a file the first is halfway through rewriting, and both
// of them commit over each other.
//
// **A folder is held until the card is Completed, not until its turn ends.** A card parked in Your
// turn still owns a live session sitting at its prompt and whatever it left in the tree, so the
// folder is no freer than it was mid-turn. Executing and Your turn are exactly the machine region;
// Preparing, Ready and Completed hold nothing.
//
// **The exemption lives on the card being started**, never on the one already there: "run this one
// anyway" is a decision about the task being launched, and the card holding the folder has no say
// in what the user chooses to run beside it.
public static class WorkingDirConflict
{
    public static bool Holds(Card card)
        => card.IsOnBoard && card.Column is BoardColumn.Executing or BoardColumn.YourTurn;

    // Null when nothing is in the way — including when the guard is off, when this card is exempt,
    // and when the launch is not a Ready one at all. A retry and a re-attach belong to a session
    // that already holds the folder, so applying the guard to them would strand the card occupying
    // it behind another card it is itself the reason for.
    public static Card? Blocking(
        Card card,
        IEnumerable<Card> cards,
        IWorkingDirectories directories,
        bool enforced)
    {
        if (!enforced || card.AllowConcurrentWorkingDir || card.NoWorkingDir
            || card.Column is not BoardColumn.Ready)
            return null;

        if (Key(card, directories) is not { } folder)
            return null;

        // `!other.NoWorkingDir` as well as the asking card's, and for a different reason: a no-folder card
        // holds ACT's scratch directory and nothing else, so it cannot be standing in the way of a real
        // one. In practice its `WorkingDir` is blank and `Key` would answer null anyway — but the flag is
        // what states it, and a card that still carries a stale path must not block on the strength of it.
        return cards.FirstOrDefault(other => other.Id != card.Id
            && !other.NoWorkingDir
            && Holds(other)
            && Key(other, directories) is { } held
            && string.Equals(held, folder, Comparison));
    }

    // The same comparison without the column rule, for the one caller that has already decided what
    // holding means: the queue runner, judging a card against the cards it is about to launch this
    // pass — none of which is Executing yet, so `Blocking` would answer no about every one of them.
    //
    // **The no-folder exemption has to be repeated here**, not only in `Blocking`. Every no-folder task
    // shares the one scratch directory, so without it a pass holding two of them would launch one and
    // hold the other back — the same symptom the flag exists to remove, one layer further down where
    // nothing on screen would explain it.
    public static bool SameFolder(Card card, Card other, IWorkingDirectories directories)
        => !card.NoWorkingDir
            && !other.NoWorkingDir
            && Key(card, directories) is { } folder
            && Key(other, directories) is { } held
            && string.Equals(held, folder, Comparison);

    // Resolved before comparing, because `~/dev/act` and `C:\dev\act\` are one folder and a guard
    // that only catches identical typing is a guard that silently does nothing. A path that will
    // not resolve matches nothing: the launch is about to fail on it for a better reason than this.
    private static string? Key(Card card, IWorkingDirectories directories)
    {
        if (string.IsNullOrWhiteSpace(card.WorkingDir))
            return null;

        try
        {
            return directories.Resolve(card.WorkingDir).TrimEnd('/', '\\');
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException
            or PathTooLongException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
