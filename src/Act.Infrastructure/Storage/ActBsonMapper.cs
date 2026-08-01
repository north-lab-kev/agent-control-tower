using System.Globalization;
using Act.Core.Model;
using LiteDB;

namespace Act.Infrastructure.Storage;

internal static class ActBsonMapper
{
    private const string RoundTrip = "O";

    private const string TimeSpanFormat = "c";

    public static BsonMapper Create()
    {
        var mapper = new BsonMapper();

        mapper.RegisterType<DateTimeOffset>(
            value => value.ToString(RoundTrip, CultureInfo.InvariantCulture),
            bson => DateTimeOffset.ParseExact(
                bson.AsString, RoundTrip, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

        mapper.RegisterType<TimeSpan>(
            value => value.ToString(TimeSpanFormat, CultureInfo.InvariantCulture),
            bson => TimeSpan.ParseExact(bson.AsString, TimeSpanFormat, CultureInfo.InvariantCulture));

        // A stored badge or reason is a code from a vocabulary that keeps changing, so a card written
        // by an older build must still load: a name this build no longer has reads back as null,
        // which both the strip and the timeline already handle. Without this, retiring one value
        // makes every card that ever carried it unreadable — `stale` and two turn-end reasons have
        // been retired already.
        mapper.RegisterType(
            typeof(TransitionReason),
            value => value.ToString(),
            bson => Enum.TryParse<TransitionReason>(bson.AsString, out var reason)
                ? reason
                : null);

        mapper.RegisterType(
            typeof(Badge),
            value => value.ToString(),
            bson => Enum.TryParse<Badge>(bson.AsString, out var badge)
                ? badge
                : null);

        mapper.Entity<Card>()
            .Ignore(card => card.NeedsAttention)
            .Ignore(card => card.IsDeleted)
            .Ignore(card => card.IsAutoArchived)
            .Ignore(card => card.IsOnBoard);

        mapper.Entity<CardMetrics>()
            .Ignore(metrics => metrics.TokensTotal)
            .Ignore(metrics => metrics.ContextPercent);

        return mapper;
    }
}
