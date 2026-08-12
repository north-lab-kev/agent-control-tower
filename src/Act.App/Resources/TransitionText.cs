using System.Globalization;
using Act.Core.Model;

namespace Act.App.Resources;

// Renders a stored `TransitionReason` in the user's language, at display time. The store keeps codes
// precisely so this can happen late: a card that moved while the UI was in English explains itself
// in French the moment the user switches, which a translated-on-write note could never do.
//
// The resource key is the enum name — `Transition_TurnEnded` — rather than a switch, so adding
// a reason cannot silently drift from its wording. `TransitionTextTests` asserts every value
// resolves in every shipped language, which is what makes the convention safe.
public static class TransitionText
{
    private const string Prefix = "Transition_";

    public static string? Resolve(TransitionReason reason, CultureInfo? culture = null)
        => Strings.ResourceManager.GetString($"{Prefix}{reason}", culture ?? CultureInfo.CurrentUICulture);

    // What the timeline shows for one entry. A reason whose wording takes the verbatim part carries
    // a `{0}`; one that does not is unaffected by being handed an argument it never uses.
    public static string For(Transition transition, CultureInfo? culture = null)
    {
        if (transition.Reason is not { } reason)
            return transition.Note ?? string.Empty;

        var wording = Resolve(reason, culture);

        if (wording is null)
            return transition.Note ?? reason.ToString();

        return string.Format(
            culture ?? CultureInfo.CurrentCulture,
            wording,
            transition.Note ?? string.Empty);
    }
}
