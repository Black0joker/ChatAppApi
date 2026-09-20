using ChatApp.Application.Abstractions;
using ChatApp.Domain.Entities;
using ChatApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Infrastructure.Persistence.Repositories;

public sealed class ConversationRepository(ChatDbContext db) : IConversationRepository
{
    public Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Conversations
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Conversation?> GetDirectBetweenAsync(Guid userA, Guid userB, CancellationToken ct = default)
    {
        // Exactly one direct conversation per pair: both users active members, type Direct.
        var candidateIds = await db.ConversationMembers
            .Where(m => m.UserId == userA && m.LeftAt == null)
            .Select(m => m.ConversationId)
            .ToListAsync(ct);

        return await db.Conversations
            .Include(x => x.Members)
            .Where(x => x.Type == Domain.Enums.ConversationType.Direct && candidateIds.Contains(x.Id))
            .Where(x => x.Members.Any(m => m.UserId == userB && m.LeftAt == null))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Conversation>> GetForUserAsync(Guid userId, CancellationToken ct = default)
        => await db.Conversations
            .Include(x => x.Members)
            .Where(x => x.Members.Any(m => m.UserId == userId && m.LeftAt == null))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Conversation conversation, CancellationToken ct = default)
        => await db.Conversations.AddAsync(conversation, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
