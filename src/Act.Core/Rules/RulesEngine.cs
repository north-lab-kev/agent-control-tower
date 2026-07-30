using Act.Core.Events;
using Act.Core.Model;

namespace Act.Core.Rules;

// The machine-controlled half of the state model: normalized event in, column and badge out. Pure
// and agent-agnostic — it never learns which agent produced the event or how the event arrived,
// which is the whole point of normalizing at the source.
//
// Two transitions are deliberately absent because neither is a rule: **Ready → Executing** is ACT
// spawning a process (an action), and **To review → Completed** is the user's explicit call. And
// recovery out of Needs feedback is here rather than being an ACT command, because the user answers
// in the terminal — ACT only ever learns that the session moved again.
public static class RulesEngine
{
    // Outside the machine region a card is the human's, and a late or stray event must not drag it
    // back. This is what keeps a Completed card completed when a dying session reports one last
    // thing, and what stops the launch boundary from being crossed by observation.
    public static bool Governs(BoardColumn column)
        => column is BoardColumn.Executing or BoardColumn.NeedsFeedback or BoardColumn.ToReview;

    public static BoardMove? Decide(Card card, AgentEvent observed)
    {
        if (!Governs(card.Column))
            return null;

        return observed switch
        {
            // The recovery and send-back rule, and the reason activity carries more weight than its
            // name: from Needs feedback it means the user answered the prompt in the terminal, and
            // from To review that they sent the work back. ACT sees the same event either way.
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

            // Observed, never answered: the card reports that the TUI is waiting and shows a
            // read-only summary. Allowed from any machine column — a session that asks is a session
            // that is alive and blocked, wherever the board currently has it.
            PermissionRequested permission => new BoardMove(
                BoardColumn.NeedsFeedback,
                Badge.NeedsPermission,
                TransitionReason.PermissionRequested,
                Message: permission.Summary),

            QuestionAsked question => new BoardMove(
                BoardColumn.NeedsFeedback,
                Badge.NeedsAnswer,
                TransitionReason.QuestionAsked,
                Message: question.Question),

            TurnEnded turn => ForTurn(turn),

            // ACT owns the process, so this is the one failure it can report first-hand. A clean
            // exit is not a card state: the turn events already said where the work stands, and the
            // session simply being over must not overwrite that.
            ProcessExited exit when exit.ExitCode != 0 => new BoardMove(
                BoardColumn.NeedsFeedback,
                Badge.Error,
                TransitionReason.AgentExited,
                Detail: exit.ExitCode.ToString()),

            // A kill is the user's own action, so it is reported rather than treated as a fault —
            // but it still needs somewhere to land, and the card must not sit in Executing with no
            // process behind it.
            SessionKilled => new BoardMove(
                BoardColumn.NeedsFeedback,
                Badge.Killed,
                TransitionReason.SessionKilled),

            // Stale is a badge, not a move: the work may still be fine and the TUI is still there,
            // so the card stays where it is and says it has gone quiet.
            NoActivityElapsed idle when card.Column is BoardColumn.Executing
                => new BoardMove(
                    BoardColumn.Executing,
                    Badge.Stale,
                    TransitionReason.NoActivity,
                    Detail: idle.Idle.ToString()),

            _ => null,
        };
    }

    // The status file the preamble asks for is what separates "finished" from "blocked" — a bare
    // `Stop` cannot. `Unknown` is the missing-or-malformed case and routes to To review on purpose:
    // the spec's rule is never to trap a finished task in limbo, so the fallback favours the user
    // seeing work they can review over a card stuck waiting for an answer nobody was asked for.
    private static BoardMove ForTurn(TurnEnded turn) => turn.Outcome switch
    {
        TurnOutcome.NeedsInput => new BoardMove(
            BoardColumn.NeedsFeedback,
            Badge.NeedsAnswer,
            TransitionReason.TurnNeedsInput,
            Message: turn.Question),

        TurnOutcome.ReadyForReview => new BoardMove(
            BoardColumn.ToReview,
            Badge.Idle,
            TransitionReason.TurnReadyForReview),

        _ => new BoardMove(
            BoardColumn.ToReview,
            Badge.Idle,
            TransitionReason.TurnWithoutStatusFile),
    };
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
    string? Detail = null,
    string? Message = null)
{
    public bool ChangesAnything(Card card) => card.Column != Column || card.Badge != Badge;
}
