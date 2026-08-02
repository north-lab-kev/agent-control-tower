using Act.App.Resources;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;

namespace Act.App.Cards;

// Holds every card the store has, including the ones off the board, and decides which of them
// anything is allowed to see. The board and the attention count are about *live* work, so they never
// look at a deleted or auto-archived card; the archive looks at nothing else.
public sealed class BoardState(ICardStore store, IClock clock)
{
    private IReadOnlyList<Card> cards = [];

    // The column each card was last seen in, so `UpdateAsync` can tell an arrival from an ordinary
    // save. Rebuilt on every load, because that is when the instances are replaced.
    private readonly Dictionary<Guid, BoardColumn> placed = [];

    public event Action? Changed;

    // Every column reads the same way — the order the user put it in, arrival order until they do —
    // and Your turn is no exception: the badge says *why* a card is waiting, and ranking by it took
    // the order out of the user's hands for the one column where the next thing to look at is their
    // call. See `CardOrder`.
    public IReadOnlyList<Card> In(BoardColumn column)
        => CardOrder.Sort(cards.Where(card => card.IsOnBoard && card.Column == column));

    public IReadOnlyList<Card> Archived
        => [.. cards.Where(card => !card.IsOnBoard).OrderByDescending(card => card.DeletedAt ?? card.ArchivedAt)];

    // Unfiltered, everything off the board included — for the one caller that is not a view: the
    // settings migration, which is looking for what a user once typed and does not care where the
    // card ended up.
    public IReadOnlyList<Card> All => cards;

    public bool HasArchived => cards.Any(card => !card.IsOnBoard);

    // Archived cards are still addressable: the archive links to them, and a restore has to be able
    // to find one. Callers that care ask `IsOnBoard`.
    public Card? Card(Guid id) => cards.FirstOrDefault(card => card.Id == id);

    // The mid-flight cards whose terminal died while their binding survived — see `SessionRestore`.
    public IReadOnlyList<Card> RestorableUnattended
        => [.. cards.Where(SessionRestore.RestoresUnattended)];

    public IReadOnlyList<Card> ChildrenOf(Card card)
        => [.. card.Children.Select(Card).OfType<Card>()];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        cards = await store.GetAllAsync(cancellationToken);

        placed.Clear();

        foreach (var card in cards)
            placed[card.Id] = card.Column;

        Changed?.Invoke();
    }

    public async Task CreateAsync(Card card, CancellationToken cancellationToken = default)
    {
        Arriving(card);

        await store.AddAsync(card, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    public async Task UpdateAsync(Card card, CancellationToken cancellationToken = default)
    {
        Arriving(card);

        await store.UpdateAsync(card, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    // A card that lands in a column lands at the end of it. Decided here rather than at each of the
    // half-dozen places a column is assigned — the launch, the rules engine, the sign-off, the
    // reopen, a hand drag — because every one of them saves through this method, and a stamp that
    // only *most* of them remembered would leave cards sitting wherever their last column had put
    // them.
    private void Arriving(Card card)
    {
        if (placed.TryGetValue(card.Id, out var was) && was == card.Column)
            return;

        card.Order = CardOrder.Last(In(card.Column), card);
    }

    // Manual ordering inside a column, which for Ready is also the order the queue will launch in:
    // the runner takes that column exactly as it is drawn. Writes only the strips that actually
    // moved, then reloads once.
    public async Task ReorderAsync(Card card, Card target, CancellationToken cancellationToken = default)
    {
        if (card.Id == target.Id || card.Column != target.Column)
            return;

        var moved = CardOrder.Move(In(card.Column), card, target);

        if (moved.Count == 0)
            return;

        foreach (var affected in moved)
            await store.UpdateAsync(affected, cancellationToken);

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
        if (card.IsAutoArchived)
            card.KeepOnBoard = true;

        card.DeletedAt = null;
        card.ArchivedAt = null;

        await store.UpdateAsync(card, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    public async Task ApplyRetentionAsync(TimeSpan? window, CancellationToken cancellationToken = default)
    {
        if (window is not { } age)
            return;

        var due = CompletedRetention.Due(cards, age, clock.Now);

        if (due.Count == 0)
            return;

        foreach (var card in due)
        {
            card.ArchivedAt = clock.Now;

            await store.UpdateAsync(card, cancellationToken);
        }

        await LoadAsync(cancellationToken);
    }

    // The only place a card actually leaves the store. Unlinks each one from any parent that is
    // still around, because `children` and `parentId` are stored on both sides and a purge that
    // skipped this would leave live cards pointing at rows that no longer exist.
    public async Task PurgeArchivedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var card in cards.Where(card => !card.IsOnBoard).ToList())
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
