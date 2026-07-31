using Act.Core.Model;

namespace Act.Core.Rules;

// Which cards are owed a terminal they no longer have. A pty is a child of ACT's process, so every
// session dies with the app while its binding in the store does not — and the same gap opens when
// the CLI exits on its own, when the user kills it, and when signing a card off ends it. Restoring is
// therefore *resuming a session id*, never starting work: a card with no binding is left alone,
// because starting one from an initial prompt is a launch and launches stay the user's call.
public static class SessionRestore
{
    // Opening a card's terminal. The whole machine region — Executing is mid-flight, and Your turn is
    // where every reason to talk to a session collects: the answer a prompt waits for can only be
    // typed into the terminal that is missing, and a send-back after review is made the same way.
    // Completed too, and for a different reason: the session *is* the record of what was done, and a
    // signed-off card that opens on a blank pane loses it. Resuming does not un-complete anything —
    // the rules do not govern Completed, so nothing the resumed session says can move the card.
    public static bool IsResumable(Card card)
        => !card.IsDeleted
            && card.SessionId is { Length: > 0 }
            && (RulesEngine.Governs(card.Column) || card.Column is BoardColumn.Completed);

    // Startup's narrower half, for cards nobody has asked to see. Only live work comes back by
    // itself: spawning a pty for every signed-off card would be a fleet of processes for work that
    // is over, so Completed waits to be opened.
    public static bool RestoresUnattended(Card card)
        => IsResumable(card) && RulesEngine.Governs(card.Column);
}
