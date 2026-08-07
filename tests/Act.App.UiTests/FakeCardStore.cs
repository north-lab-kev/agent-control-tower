using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.App.UiTests;

internal sealed class FakeCardStore(IEnumerable<Card> cards) : ICardStore
{
    private readonly List<Card> rows = [.. cards];

    public Task<IReadOnlyList<Card>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Card>>([.. rows]);

    public Task<Card?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(rows.FirstOrDefault(row => row.Id == id));

    public Task AddAsync(Card card, CancellationToken cancellationToken = default)
    {
        rows.Add(card);

        return Task.CompletedTask;
    }

    // The cards handed to `UpdateAsync`, in order. A fake that only holds rows cannot tell "the
    // board wrote this down" from "the board mutated the object it was already holding", and the
    // arming the queue resolves has to be *written* before anything launches.
    public List<Card> Updated { get; } = [];

    public Task UpdateAsync(Card card, CancellationToken cancellationToken = default)
    {
        Updated.Add(card);

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(rows.RemoveAll(row => row.Id == id) > 0);

    public Task<int> NextNumberAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(rows.Count + 1);
}

internal sealed class FrozenClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now => now;
}

internal sealed class FakeSettingsStore : ISettingsStore
{
    private UserSettings settings = new();

    public UserSettings Load() => settings;

    public void Save(UserSettings updated) => settings = updated;
}

internal sealed class FakeSleepInhibitor : ISleepInhibitor
{
    public bool IsHeld { get; private set; }

    public void Hold() => IsHeld = true;

    public void Release() => IsHeld = false;
}
