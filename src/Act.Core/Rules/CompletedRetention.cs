using Act.Core.Model;

namespace Act.Core.Rules;

public static class CompletedRetention
{
    public const int MinimumDays = 1;

    public const int MaximumDays = 365;

    public static int ClampDays(int days) => Math.Clamp(days, MinimumDays, MaximumDays);

    public static TimeSpan Window(int days) => TimeSpan.FromDays(ClampDays(days));

    public static bool IsDue(Card card, TimeSpan window, DateTimeOffset now)
        => card is { Column: BoardColumn.Completed, IsDeleted: false, IsAutoArchived: false, KeepOnBoard: false }
            && card.CompletedAt is { } completed
            && now - completed >= window;

    public static IReadOnlyList<Card> Due(IEnumerable<Card> cards, TimeSpan window, DateTimeOffset now)
        => [.. cards.Where(card => IsDue(card, window, now))];
}
