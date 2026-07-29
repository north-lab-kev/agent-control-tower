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

        mapper.Entity<Card>()
            .Ignore(card => card.NeedsAttention)
            .Ignore(card => card.IsDeleted);

        mapper.Entity<CardMetrics>()
            .Ignore(metrics => metrics.TokensTotal)
            .Ignore(metrics => metrics.ContextPercent);

        return mapper;
    }
}
