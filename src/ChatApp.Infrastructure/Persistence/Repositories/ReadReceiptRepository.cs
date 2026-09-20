using ChatApp.Application.Abstractions;
using ChatApp.Domain.Entities;
using ChatApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Infrastructure.Persistence.Repositories;

public sealed class ReadReceiptRepository(ChatDbContext db) : IReadReceiptRepository
{
    public Task<MessageReadReceipt?> GetAsync(Guid messageId, Guid userId, CancellationToken ct = default)
        => db.MessageReadReceipts.FirstOrDefaultAsync(x => x.MessageId == messageId && x.UserId == userId, ct);

    public async Task<IReadOnlyList<MessageReadReceipt>> GetForMessagesAsync(IEnumerable<Guid> messageIds, CancellationToken ct = default)
    {
        var ids = messageIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];
        return await db.MessageReadReceipts
            .AsNoTracking()
            .Where(x => ids.Contains(x.MessageId))
            .ToListAsync(ct);
    }

    public async Task AddAsync(MessageReadReceipt receipt, CancellationToken ct = default)
        => await db.MessageReadReceipts.AddAsync(receipt, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
