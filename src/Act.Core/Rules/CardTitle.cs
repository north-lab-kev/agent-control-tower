using Act.Core.Agents;
using Act.Core.Model;

namespace Act.Core.Rules;

// What a card is *called* on screen, which stopped being the same thing as what it stores the day
// title generation became optional. With `UserSettings.GenerateTitles` off, a save that leaves the
// box blank stores a blank title — the user said not to write one for them, and inventing one anyway
// would be the setting doing nothing. So the prompt's opening words stand in at render time instead,
// exactly as they do while a generated title is still out with the agent: derived, never stored, and
// replaced the moment a real title is typed.
//
// Every surface that shows a title goes through here rather than reading `Card.Title`, because a
// nameless strip is unrecognisable and no screen repairs it. Search does not need it — `CardSearch`
// already matches the prompt.
public static class CardTitle
{
    public static string Of(Card card)
    {
        if (!string.IsNullOrWhiteSpace(card.Title))
            return card.Title;

        return string.IsNullOrWhiteSpace(card.InitialPrompt)
            ? string.Empty
            : TaskTitleQuery.FromPrompt(card.InitialPrompt);
    }
}
