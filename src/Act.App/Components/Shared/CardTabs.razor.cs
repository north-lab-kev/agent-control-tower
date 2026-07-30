using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Shared;

// Shown on every face of a card so the set is always reachable from any of them. The card's state
// chooses the landing page; it does not decide what you are allowed to look at afterwards, so this
// appears whatever column the card is in.
public partial class CardTabs
{
    [Parameter, EditorRequired]
    public Guid CardId { get; set; }

    public static string Route(Guid cardId, CardSurface surface) => surface switch
    {
        CardSurface.Terminal => $"/card/{cardId}/terminal",
        CardSurface.Timeline => $"/card/{cardId}/timeline",
        _ => $"/card/{cardId}/edit",
    };

    private string Href(CardSurface surface) => Route(CardId, surface);
}
