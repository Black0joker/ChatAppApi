using ChatApp.Domain.Entities;

namespace ChatApp.Application.Abstractions;

public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Conversation?> GetDirectBetweenAsync(Guid userA, Guid userB, CancellationToken ct = default);
    Task<IReadOnlyList<Conversation>> GetForUserAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Conversation conversation, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
