using Act.App.Resources;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;

namespace Act.App.Cards;

// Holds every card the store has, including the deleted ones, and decides which of them anything
// is allowed to see. The board and the attention count are about *live* work, so they never look
// at deleted cards; the archive looks at nothing else.
public sealed class BoardState(ICardStore store, IClock clock)
{
    private IReadOnlyList<Card> cards = [];

    public event Action? Changed;

    // Your turn is the one column whose cards are there for different reasons, so it is ordered by
    // how much the waiting costs rather than by insertion — see `AttentionOrder`.
    public IReadOnlyList<Card> In(BoardColumn column)
    {
        var live = cards.Where(card => !card.IsDeleted && card.Column == column);

        return column is BoardColumn.YourTurn
            ? [.. live.OrderBy(card => AttentionOrder.Rank(card.Badge))]
            : [.. live];
    }

    public IReadOnlyList<Card> Archived
        => [.. cards.Where(card => card.IsDeleted).OrderByDescending(card => card.DeletedAt)];

    public bool HasArchived => cards.Any(card => card.IsDeleted);

    // Deleted cards are still addressable: the archive links to them, and a restore has to be able
    // to find one. Callers that care ask `IsDeleted`.
    public Card? Card(Guid id) => cards.FirstOrDefault(card => card.Id == id);

    // The mid-flight cards whose terminal died while their binding survived — see `SessionRestore`.
    public IReadOnlyList<Card> RestorableUnattended
        => [.. cards.Where(SessionRestore.RestoresUnattended)];

    public IReadOnlyList<Card> ChildrenOf(Card card)
        => [.. card.Children.Select(Card).OfType<Card>()];

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

    // The copy is a new card in every sense the store cares about — its own id and number — so it
    // goes through the same create path, and the caller gets it back to open it.
    public async Task<Card> DuplicateAsync(Card card, CancellationToken cancellationToken = default)
    {
        var copy = CardDuplicate.Of(card, Text.Format(Strings.Task_DuplicateTitle, card.Title), clock.Now);

        await CreateAsync(copy, cancellationToken);

        return copy;
    }

    // Soft: the row stays, marked with a timestamp, and the archive can put it back. Lineage is
    // left intact on purpose — a restored parent should find its children still attached, which
    // means an archived card may be the parent of a live one, and that is fine.
    public async Task DeleteAsync(
        Card card,
        bool includeChildren,
        CancellationToken cancellationToken = default)
    {
        foreach (var target in includeChildren ? Subtree(card) : [card])
        {
            if (target.IsDeleted)
                continue;

            target.DeletedAt = clock.Now;

            await store.UpdateAsync(target, cancellationToken);
        }

        await LoadAsync(cancellationToken);
    }

    // Restores one card, not its subtree: children were archived as their own decision and are
    // restored the same way, so bringing a parent back never silently resurrects work.
    public async Task RestoreAsync(Card card, CancellationToken cancellationToken = default)
    {
        card.DeletedAt = null;

        await store.UpdateAsync(card, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    // The only place a card actually leaves the store. Unlinks each one from any parent that is
    // still around, because `children` and `parentId` are stored on both sides and a purge that
    // skipped this would leave live cards pointing at rows that no longer exist.
    public async Task PurgeArchivedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var card in cards.Where(card => card.IsDeleted).ToList())
        {
            if (Card(card.ParentId ?? Guid.Empty) is { } parent && parent.Children.Remove(card.Id))
                await store.UpdateAsync(parent, cancellationToken);

            await store.DeleteAsync(card.Id, cancellationToken);
        }

        await LoadAsync(cancellationToken);
    }

    public async Task MoveAsync(Card card, BoardColumn target, CancellationToken cancellationToken = default)
    {
        if (!ManualMove.IsAllowed(card.Column, target))
            throw new InvalidOperationException(
                $"Card {card.Number} cannot be moved by hand from {card.Column} to {target}.");

        card.Column = target;
        card.Transitions.Add(new Transition
        {
            At = clock.Now,
            Column = target,
            Reason = TransitionReason.MovedByHand,
        });

        await UpdateAsync(card, cancellationToken);
    }

    // Depth-first and cycle-guarded. Lineage is stored on both sides, so a corrupt pair could form
    // a loop; a delete is the wrong moment to hang.
    private List<Card> Subtree(Card root)
    {
        var seen = new HashSet<Guid>();
        var ordered = new List<Card>();
        var pending = new Stack<Card>([root]);

        while (pending.TryPop(out var card))
        {
            if (!seen.Add(card.Id))
                continue;

            ordered.Add(card);

            foreach (var child in ChildrenOf(card))
                pending.Push(child);
        }

        return ordered;
    }
}
