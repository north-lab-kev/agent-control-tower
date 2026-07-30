using Act.Core.Model;

namespace Act.Core.Abstractions;

public interface ICardStore
{
    Task<IReadOnlyList<Card>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Card?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(Card card, CancellationToken cancellationToken = default);

    Task UpdateAsync(Card card, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> NextNumberAsync(CancellationToken cancellationToken = default);
}
