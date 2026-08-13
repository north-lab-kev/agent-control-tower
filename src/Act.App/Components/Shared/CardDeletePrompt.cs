using Act.App.Resources;
using Act.Core.Model;
using Act.Core.Rules;
using Radzen;

namespace Act.App.Components.Shared;

// The question asked before a delete that ends a live agent, shared by the board's strip and the task
// page. Both delete the same card the same way, so both owe the same warning — the board asking and the
// task page not was a real gap, and the fix is one call rather than two policies.
//
// **The rule check lives inside**, so a caller cannot obey the dialog and forget `CardDeleteConfirm`.
// A card that needs no question answers true immediately, which makes the call site one line either way.
//
// **The number, and not the title.** Saying which card is the point — on a board of a dozen strips, "are
// you sure" beside the wrong one is how the wrong agent gets stopped — but a title dropped into the
// sentence reads as part of it: *"#1000 Calculate the sum of one and two has an agent working on it"*.
// The number is the card's identity everywhere else in ACT, and it cannot collide with the prose.
internal static class CardDeletePrompt
{
    public static async Task<bool> ConfirmedAsync(DialogService dialogs, Card card)
    {
        if (!CardDeleteConfirm.IsRequired(card.Column))
            return true;

        var answer = await dialogs.Confirm(
            Text.Format(Strings.Card_DeleteConfirm, card.Number),
            Strings.Task_Delete,
            new ConfirmOptions
            {
                OkButtonText = Strings.Card_DeleteConfirm_Ok,
                CancelButtonText = Strings.Card_DeleteConfirm_Cancel,
                CssClass = "act-dialog",
            });

        // Null when dismissed with the X or the overlay, which means the same as Cancel. Anything but a
        // plain yes keeps the card, because the cost of reading a dismissal as consent is a stopped agent.
        return answer is true;
    }
}
