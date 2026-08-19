namespace Act.Core.Agents;

// What an interrupt looks like on one CLI: the keys that stop a turn, and the characters that open a
// picker able to swallow one of those keys before the turn ever sees it. Both are adapter facts — a
// keybinding and a composer behaviour — and they travel together because neither is usable alone.
//
// `None` is the honest default for an agent whose interrupt has never been measured: it reports
// nothing, which is exactly how every card behaved before any of this existed.
public sealed record TurnInterruptProfile(
    IReadOnlyCollection<string> Keys,
    IReadOnlyCollection<char> PickerTriggers)
{
    public static readonly TurnInterruptProfile None = new([], []);
}
