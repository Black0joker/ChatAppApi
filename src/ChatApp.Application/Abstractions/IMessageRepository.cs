using ChatApp.Domain.Entities;

namespace ChatApp.Application.Abstractions;

public interface IMessageRepository
{
    Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Idempotency lookup (PLAN §24): sender-scoped client key.</summary>
    Task<Message?> GetBySenderAndClientIdAsync(Guid senderId, Guid clientMessageId, CancellationToken ct = default);

    /// <summary>
    /// Keyset page of a conversation, newest first.
    /// Cursors are (CreatedAt, Id) of a known message; at most one of before/after applies.
    /// Returns up to <paramref name="limit"/>+1 items so callers can derive HasMore.
    /// </summary>
    Task<IReadOnlyList<Message>> GetHistoryAsync(
        Guid conversationId,
        DateTimeOffset? beforeCreatedAt, Guid? beforeId,
        DateTimeOffset? afterCreatedAt, Guid? afterId,
        int limit,
        CancellationToken ct = default);

    Task AddAsync(Message message, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
