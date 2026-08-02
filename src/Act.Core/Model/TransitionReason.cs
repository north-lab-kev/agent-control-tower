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

    // The recovery and send-back row: the user acted in the terminal and ACT saw the session move.
    ActivityObserved,
    CompactingStarted,
    CompactingFinished,
    PermissionRequested,
    QuestionAsked,
    TurnEnded,
    TurnFailed,
    AgentExited,
    SessionKilled,

    // The CLI is parked on its directory-trust prompt, which blocks before the session exists and
    // so reaches ACT as no hook at all. Its own reason rather than `PermissionRequested`: nothing
    // was requested by an agent that has not started, and the timeline should say which prompt it
    // was.
    StartupPrompt,
}
