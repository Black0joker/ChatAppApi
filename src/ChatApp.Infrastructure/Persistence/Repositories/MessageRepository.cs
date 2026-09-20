using ChatApp.Application.Abstractions;
using ChatApp.Domain.Entities;
using ChatApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Infrastructure.Persistence.Repositories;

public sealed class MessageRepository(ChatDbContext db) : IMessageRepository
{
    public Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Messages.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Message?> GetBySenderAndClientIdAsync(Guid senderId, Guid clientMessageId, CancellationToken ct = default)
        => db.Messages.FirstOrDefaultAsync(
            x => x.SenderId == senderId && x.ClientMessageId == clientMessageId, ct);

    public async Task<IReadOnlyList<Message>> GetHistoryAsync(
        Guid conversationId,
        DateTimeOffset? beforeCreatedAt, Guid? beforeId,
        DateTimeOffset? afterCreatedAt, Guid? afterId,
        int limit,
        CancellationToken ct = default)
    {
        // Keyset pagination (PLAN §7): no OFFSET, stable under concurrent inserts.
        // Ordering is (CreatedAt DESC, Id DESC); the tiebreaker keeps cursors deterministic
        // when two messages share a timestamp.
        var q = db.Messages
            .Where(x => x.ConversationId == conversationId);

        if (beforeCreatedAt.HasValue && beforeId.HasValue)
            q = q.Where(x => x.CreatedAt < beforeCreatedAt
                || (x.CreatedAt == beforeCreatedAt && x.Id.CompareTo(beforeId.Value) < 0));

        if (afterCreatedAt.HasValue && afterId.HasValue)
            q = q.Where(x => x.CreatedAt > afterCreatedAt
                || (x.CreatedAt == afterCreatedAt && x.Id.CompareTo(afterId.Value) > 0));

        return await q
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(limit + 1) // +1 probes HasMore
            .ToListAsync(ct);
    }

    public async Task AddAsync(Message message, CancellationToken ct = default)
        => await db.Messages.AddAsync(message, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
