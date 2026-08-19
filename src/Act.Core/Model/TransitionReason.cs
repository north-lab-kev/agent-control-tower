namespace Act.Core.Model;

// Why a card moved, as a code rather than a sentence. Two reasons, and the second is the one that
// makes it a rule rather than a preference:
//
//   * `Act.Core` has no resources and must not acquire any — wording belongs to the UI layer.
//   * A transition is *stored*. Persisting a localized string would freeze the language it was
//     written in, so a card moved before the user switched to French would still explain itself in
//     English forever. Codes go in the store; translation happens at render time.
//
// `Transition.Note` stays alongside this for the verbatim part — an exit code, a CLI error, the
// adjustments a launch made — which is data rather than wording and is not translatable.
public enum TransitionReason
{
    Launched,
    LaunchedWithAdjustments,
    Retried,
    RetriedWithAdjustments,
    LaunchFailed,
    MovedByHand,

    // Your turn → Completed, the user's explicit sign-off. Its own reason rather than a hand-move:
    // it is the only transition that ends a card's session, and the timeline should say so.
    CompletedByHand,

    // Completed → Your turn, the sign-off taken back — the user had more to say. Its own reason for
    // the same kind of reason: it un-stamps the sign-off, which no plain move does.
    Reopened,

    // The terminal came back for a card that never left its column: the process had died with the
    // app, with a kill or with the CLI itself, and the stored binding was resumed into a fresh one.
    SessionRestored,
    RestoreFailed,

    // The user asked for a fresh terminal on a session that was still running. Distinct from
    // `SessionRestored`, which reacts to a process that had already died: here ACT ended a live one
    // on purpose, so the timeline has to say the scrollback was discarded rather than lost.
    TerminalRestarted,

    // The recovery and send-back row: the user acted in the terminal and ACT saw the session move.
    ActivityObserved,
    CompactingStarted,
    CompactingFinished,
    PermissionRequested,
    QuestionAsked,
    TurnEnded,
    TurnFailed,

    // The turn the user stopped with a keystroke. Its own reason rather than `TurnEnded`: no hook
    // reports an interrupt, so this row is the one ACT writes off its own input channel, and a
    // timeline that says which is a timeline that can be checked against the CLI.
    TurnInterrupted,
    AgentExited,

    // The parent's row when its agent creates a follow-up. It carries no column, because the parent
    // did not move — it produced something — and `TimelineEntry` renders a reasoned row with a null
    // column as wording alone.
    SpawnedFollowUp,

    // The CLI is parked on its directory-trust prompt, which blocks before the session exists and
    // so reaches ACT as no hook at all. Its own reason rather than `PermissionRequested`: nothing
    // was requested by an agent that has not started, and the timeline should say which prompt it
    // was.
    StartupPrompt,
}
