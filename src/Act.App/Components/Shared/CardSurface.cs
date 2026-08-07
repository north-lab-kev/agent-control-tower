namespace Act.App.Components.Shared;

// The faces of a card. Which one a click opens is decided by the card's state; which one you are
// looking at afterwards is not — every face stays reachable from every other.
public enum CardSurface
{
    Task,
    Terminal,
    Timeline,
}
