namespace Act.App.Components.Shared;

// The two faces of a card. Which one a click opens is decided by the card's state; which one you
// are looking at afterwards is not — you can always switch.
public enum CardSurface
{
    Task,
    Terminal,
}
