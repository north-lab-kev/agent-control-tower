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
    LaunchFailed,
    MovedByHand,

    // The recovery and send-back row: the user acted in the terminal and ACT saw the session move.
    ActivityObserved,
    CompactingStarted,
    CompactingFinished,
    PermissionRequested,
    QuestionAsked,
    TurnReadyForReview,
    TurnNeedsInput,
    TurnWithoutStatusFile,
    AgentExited,
    SessionKilled,
    NoActivity,
}
