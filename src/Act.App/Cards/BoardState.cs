using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;

namespace Act.App.Cards;

public sealed class BoardState(ICardStore store)
{
    private IReadOnlyList<Card> cards = [];

    public event Action? Changed;

    public int AttentionCount => cards.Count(card => card.NeedsAttention);

    public IReadOnlyList<Card> In(BoardColumn column)
        => [.. cards.Where(card => card.Column == column)];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        cards = await store.GetAllAsync(cancellationToken);

        Changed?.Invoke();
    }

    public async Task CreateAsync(Card card, CancellationToken cancellationToken = default)
    {
        await store.AddAsync(card, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    public async Task UpdateAsync(Card card, CancellationToken cancellationToken = default)
    {
        await store.UpdateAsync(card, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    public async Task MoveAsync(Card card, BoardColumn target, CancellationToken cancellationToken = default)
    {
        if (!ManualMove.IsAllowed(card.Column, target))
            throw new InvalidOperationException(
                $"Card {card.Number} cannot be moved by hand from {card.Column} to {target}.");

        card.Column = target;
        card.Transitions.Add(new Transition { At = DateTimeOffset.Now, Column = target });

        await UpdateAsync(card, cancellationToken);
    }
}
