using Act.Core.Abstractions;
using Act.Core.Model;
using LiteDB;

namespace Act.Infrastructure.Storage;

internal sealed class LiteDbCardStore(ILiteDatabase database) : ICardStore
{
    private const string CounterId = "cardNumber";

    private const int FirstNumber = 1000;

    private readonly Lock numberGate = new();

    public Task<IReadOnlyList<Card>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Card>>([.. Cards().FindAll().OrderByDescending(card => card.Number)]);

    public Task<Card?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult<Card?>(Cards().FindById(id));

    public Task AddAsync(Card card, CancellationToken cancellationToken = default)
    {
        if (card.Number == 0)
            card.Number = MintNumber();

        Cards().Insert(card);

        return Task.CompletedTask;
    }

    public Task UpdateAsync(Card card, CancellationToken cancellationToken = default)
    {
        if (!Cards().Update(card))
            throw new InvalidOperationException($"Card {card.Id} is not in the store, so it cannot be updated.");

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Cards().Delete(id));

    public Task<int> NextNumberAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(MintNumber());

    private int MintNumber()
    {
        lock (numberGate)
        {
            var counters = Counters();
            var stored = counters.FindById(CounterId);

            var next = stored is null
                ? Math.Max(FirstNumber, HighestNumber() + 1)
                : stored.Value + 1;

            counters.Upsert(new NumberCounter { Id = CounterId, Value = next });

            return next;
        }
    }

    private int HighestNumber()
        => Cards().FindAll().Select(card => card.Number).DefaultIfEmpty(0).Max();

    private ILiteCollection<Card> Cards()
        => database.GetCollection<Card>(ActCollections.Cards);

    private ILiteCollection<NumberCounter> Counters()
        => database.GetCollection<NumberCounter>(ActCollections.Counters);

    private sealed class NumberCounter
    {
        public string Id { get; set; } = string.Empty;

        public int Value { get; set; }
    }
}
