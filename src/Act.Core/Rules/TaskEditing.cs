using Act.Core.Model;

namespace Act.Core.Rules;

// What an edit may still change once a card has been launched. The boundary is the launch itself,
// read off the column like every other rule here — a card in Preparing or Ready has never had a
// session, so nothing it says has been acted on yet and all of it is still a proposal.
//
// Past that line the split is between what the run *was* and what the next one *will be*:
//
//   * Frozen — `initialPrompt`, working dir and agent, because they describe the work that was
//     actually asked for and a session already exists that was started from them. The spec makes
//     `initialPrompt` immutable for exactly this: it is what a re-run repeats and what an audit
//     reads. Schedule and the git action go with them — both are launch-time decisions, and this
//     card has launched, so editing them would change nothing while claiming otherwise.
//   * Still open — the title, which names the card rather than the work, and everything in
//     `launchConfig`. A model or permission mode does not apply to the turn already running, but
//     it is what the next launch uses, which is the whole point of being allowed to switch model
//     on a failed card before retrying it.
public static class TaskEditing
{
    public static bool CanEditLaunchInputs(Card card)
        => card.Column is BoardColumn.Preparing or BoardColumn.Ready;
}
