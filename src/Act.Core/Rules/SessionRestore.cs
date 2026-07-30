using Act.Core.Model;

namespace Act.Core.Rules;

// Which cards are owed a terminal they no longer have. A pty is a child of ACT's process, so every
// session dies with the app while its binding in the store does not — and the same gap opens when
// the CLI exits on its own or the user kills it. Restoring is therefore *resuming a session id*,
// never starting work: a card with no binding is left alone, because starting one from an initial
// prompt is a launch and launches stay the user's call.
//
// The whole machine region. Executing is mid-flight, and Your turn is where every reason to talk to
// a session collects: the answer a prompt waits for can only be typed into the terminal that is
// missing, and a send-back after review is made the same way. Completed and the two columns before a
// launch are not restored: there is nothing running to bring back.
public static class SessionRestore
{
    public static bool IsResumable(Card card)
        => !card.IsDeleted
            && card.SessionId is { Length: > 0 }
            && RulesEngine.Governs(card.Column);
}
