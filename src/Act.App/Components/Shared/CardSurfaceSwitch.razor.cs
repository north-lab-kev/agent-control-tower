using Microsoft.AspNetCore.Components;

namespace Act.App.Components.Shared;

// Shown on both of a card's pages so the pair is always reachable from either side. The card's
// state chooses the landing page; it does not decide what you are allowed to look at afterwards,
// so this appears whatever column the card is in.
public partial class CardSurfaceSwitch(NavigationManager navigation)
{
    [Parameter, EditorRequired]
    public Guid CardId { get; set; }

    [Parameter, EditorRequired]
    public CardSurface Current { get; set; }

    private void GoAsync(CardSurface surface)
    {
        if (surface == Current)
            return;

        navigation.NavigateTo(surface is CardSurface.Terminal
            ? $"/card/{CardId}/terminal"
            : $"/card/{CardId}/edit");
    }
}
