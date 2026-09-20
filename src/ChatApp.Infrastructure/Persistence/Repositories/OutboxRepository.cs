using ChatApp.Application.Abstractions;
using ChatApp.Domain.Entities;
using ChatApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Infrastructure.Persistence.Repositories;

public sealed class OutboxRepository(ChatDbContext db) : IOutboxRepository
{
    public async Task AddAsync(OutboxEvent @event, CancellationToken ct = default)
        => await db.OutboxEvents.AddAsync(@event, ct);

    public async Task<IReadOnlyList<OutboxEvent>> GetPendingAsync(int batchSize, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.OutboxEvents
            .Where(x => x.ProcessedAt == null && x.NextAttemptAt <= now)
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public Task<int> DeleteProcessedBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => db.OutboxEvents
            .Where(x => x.ProcessedAt != null && x.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
