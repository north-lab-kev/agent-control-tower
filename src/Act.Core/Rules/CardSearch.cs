using System.Globalization;
using Act.Core.Model;

namespace Act.Core.Rules;

public static class CardSearch
{
    private static readonly CompareInfo Comparer = CultureInfo.InvariantCulture.CompareInfo;

    private const CompareOptions Folding = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public static bool IsActive(string? query) => Terms(query).Count > 0;

    public static IReadOnlyList<string> Terms(string? query)
        => query is null
            ? []
            : query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool Matches(Card card, string? query)
        => Terms(query).All(term => MatchesTerm(card, term));

    public static IReadOnlyList<Card> Filter(IEnumerable<Card> cards, string? query)
    {
        if (!IsActive(query))
            return [.. cards];

        var terms = Terms(query);

        return [.. cards.Where(card => terms.All(term => MatchesTerm(card, term)))];
    }

    private static bool MatchesTerm(Card card, string term)
        => MatchesNumber(card, term)
            || Contains(card.Title, term)
            || Contains(card.WorkingDir, term)
            || Contains(card.InitialPrompt, term);

    private static bool MatchesNumber(Card card, string term)
    {
        var digits = term.StartsWith('#') ? term[1..] : term;

        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            return false;

        return card.Number.ToString(CultureInfo.InvariantCulture).StartsWith(digits, StringComparison.Ordinal);
    }

    private static bool Contains(string? field, string term)
        => !string.IsNullOrEmpty(field) && Comparer.IndexOf(field, term, Folding) >= 0;
}
