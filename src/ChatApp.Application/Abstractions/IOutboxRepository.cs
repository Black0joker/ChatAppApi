using ChatApp.Domain.Entities;

namespace ChatApp.Application.Abstractions;

public interface IOutboxRepository
{
    Task AddAsync(OutboxEvent @event, CancellationToken ct = default);

    /// <summary>Due, unprocessed events, oldest first, capped at <paramref name="batchSize"/>.</summary>
    Task<IReadOnlyList<OutboxEvent>> GetPendingAsync(int batchSize, CancellationToken ct = default);

    /// <returns>Rows deleted.</returns>
    Task<int> DeleteProcessedBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
