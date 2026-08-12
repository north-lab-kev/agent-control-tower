using Act.Core.Events;
using Act.Core.Model;

namespace Act.Core.Rules;

// The machine-controlled half of the state model: normalized event in, column and badge out. Pure
// and agent-agnostic — it never learns which agent produced the event or how the event arrived,
// which is the whole point of normalizing at the source.
//
// Two transitions are deliberately absent because neither is a rule: **Ready → Executing** is ACT
// spawning a process (an action), and **Your turn → Completed** is the user's explicit call. And
// recovery out of Your turn is here rather than being an ACT command, because the user answers in
// the terminal — ACT only ever learns that the session moved again.
public static class RulesEngine
{
    // Outside the machine region a card is the human's, and a late or stray event must not drag it
    // back. This is what keeps a Completed card completed when a dying session reports one last
    // thing, and what stops the launch boundary from being crossed by observation.
    public static bool Governs(BoardColumn column)
        => column is BoardColumn.Executing or BoardColumn.YourTurn;

    public static BoardMove? Decide(Card card, AgentEvent observed)
    {
        if (!Governs(card.Column))
            return null;

        return observed switch
        {
            // A tool running is not the user approving, so it may not clear a permission block. Drawn on
            // `ToolName` because that is the only thing separating the two events: `UserPromptSubmit`
            // carries none, every tool event does. **`needs answer` is deliberately not covered** —
            // see *A tool event may not clear a permission block* in `docs/design-notes.md` for the
            // measurement behind this, the asymmetry, and the stale badge it accepts in exchange.
            ActivityObserved { ToolName: not null } when card.Badge is Badge.NeedsPermission => null,

            // The recovery and send-back rule, and the reason activity carries more weight than its
            // name: from Your turn it means the user acted in the terminal — answered the prompt, or
            // sent reviewed work back. ACT sees the same event either way, and the badge it is
            // leaving behind is the only thing that distinguished the two.
            ActivityObserved => new BoardMove(
                BoardColumn.Executing,
                Badge.Running,
                TransitionReason.ActivityObserved),

            CompactingStarted => new BoardMove(
                BoardColumn.Executing,
                Badge.Compacting,
                TransitionReason.CompactingStarted),

            CompactingFinished => new BoardMove(
                BoardColumn.Executing,
                Badge.Running,
                TransitionReason.CompactingFinished),

            // A permission request is not news to a card already holding an unanswered question, and
            // it says strictly less: the question names what the user is being asked, a permission
            // notice only that *something* is waiting. Claude Code prompts for its question tool the
            // way it prompts for any other, so both events describe the same block and the generic
            // one arrives second — unsuppressed it would replace `needs answer` with `needs
            // permission` and nothing to approve. Nothing legitimate is lost: the agent is stopped
            // until the user answers in the terminal, and that answer comes back as activity.
            PermissionRequested when card.Badge is Badge.NeedsAnswer => null,

            // Observed, never answered: the card reports only that the TUI is waiting, and the badge
            // is the whole report — what is being asked stays on the screen it is being asked on.
            // Allowed from any machine column — a session that asks is a session that is alive and
            // blocked, wherever the board currently has it.
            PermissionRequested => new BoardMove(
                BoardColumn.YourTurn,
                Badge.NeedsPermission,
                TransitionReason.PermissionRequested),

            // The pre-session prompt, and the only rule that fires on something *not* happening: a
            // CLI parked on its directory-trust screen has produced no hook to report it. Executing
            // only — a restore deliberately leaves a card where the rules last had it, and a card
            // already in Your turn is blocked on something the agent named, which says more than
            // this does. Recovery needs no rule of its own: answering the prompt lets the session
            // start, and its first hook arrives as activity.
            StartupPromptWaiting when card.Column is BoardColumn.Executing => new BoardMove(
                BoardColumn.YourTurn,
                Badge.NeedsPermission,
                TransitionReason.StartupPrompt),

            QuestionAsked => new BoardMove(
                BoardColumn.YourTurn,
                Badge.NeedsAnswer,
                TransitionReason.QuestionAsked),

            // A turn end is a turn end: nothing separates finished from blocked, so it always offers
            // the work up for review. A turn that ends while the card is blocked ends *because* that
            // block is over — the prompt was denied and the agent stopped — so review is right there
            // too, and the alternative is a card waiting on a prompt nobody will answer.
            TurnEnded => new BoardMove(
                BoardColumn.YourTurn,
                Badge.ReadyForReview,
                TransitionReason.TurnEnded),

            // The agent's own report that the turn failed, which is not the same as its process
            // failing: the CLI is still there and would exit zero, so this is the only way ACT hears
            // about an api error or a model the account cannot use. `error` rather than `to review`,
            // because there is nothing to review.
            TurnFailed failure => new BoardMove(
                BoardColumn.YourTurn,
                Badge.Error,
                TransitionReason.TurnFailed,
                Detail: failure.Reason),

            // ACT owns the process, so this is the one failure it can report first-hand. A clean
            // exit is not a card state: the turn events already said where the work stands, and the
            // session simply being over must not overwrite that.
            ProcessExited exit when exit.ExitCode != 0 => new BoardMove(
                BoardColumn.YourTurn,
                Badge.Error,
                TransitionReason.AgentExited,
                Detail: exit.ExitCode.ToString()),

            // Silence is deliberately not a rule. A card that has gone quiet gets no badge and no
            // transition, because ACT cannot tell a hung agent from a forty-minute build from its
            // own ingestion having broken — see `QuietSession`, which states the gap instead of
            // guessing at its cause.
            _ => null,
        };
    }
}

// `Reason` is a code the UI translates, never a sentence — nothing here may produce display text,
// because a transition is stored and a stored translation would freeze the language it was written
// in. `Detail` is the verbatim part (an exit code, an idle duration); `Message` is what the agent
// itself said, and it is read-only in the strictest sense: ACT shows it and offers no way to answer
// it, because the answer belongs in the terminal.
public sealed record BoardMove(
    BoardColumn Column,
    Badge Badge,
    TransitionReason Reason,
    string? Detail = null)
{
    public bool ChangesAnything(Card card) => card.Column != Column || card.Badge != Badge;
}
