using Act.Core.Abstractions;

namespace Act.App.Seeding;

public sealed class SampleCardSeeder(ICardStore store)
{
    public async Task SeedIfEmptyAsync(CancellationToken cancellationToken = default)
    {
        var stored = await store.GetAllAsync(cancellationToken);
        if (stored.Count > 0)
            return;

        foreach (var card in SampleCards.Create())
            await store.AddAsync(card, cancellationToken);
    }
}
