using ChatApp.Application.Abstractions;
using ChatApp.Domain.Entities;
using ChatApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Infrastructure.Persistence.Repositories;

public sealed class AttachmentRepository(ChatDbContext db) : IAttachmentRepository
{
    public Task<MessageAttachment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.MessageAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<MessageAttachment>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0)
            return [];
        return await db.MessageAttachments.Where(x => list.Contains(x.Id)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MessageAttachment>> GetForMessagesAsync(IEnumerable<Guid> messageIds, CancellationToken ct = default)
    {
        var list = messageIds.Distinct().ToList();
        if (list.Count == 0)
            return [];
        return await db.MessageAttachments
            .AsNoTracking()
            .Where(x => x.MessageId.HasValue && list.Contains(x.MessageId.Value))
            .ToListAsync(ct);
    }

    public async Task AddAsync(MessageAttachment attachment, CancellationToken ct = default)
        => await db.MessageAttachments.AddAsync(attachment, ct);

    public Task RemoveAsync(MessageAttachment attachment, CancellationToken ct = default)
    {
        db.MessageAttachments.Remove(attachment);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
