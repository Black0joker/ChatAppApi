using ChatApp.Domain.Entities;

namespace ChatApp.Application.Abstractions;

public interface IAttachmentRepository
{
    Task<MessageAttachment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<MessageAttachment>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<MessageAttachment>> GetForMessagesAsync(IEnumerable<Guid> messageIds, CancellationToken ct = default);
    Task AddAsync(MessageAttachment attachment, CancellationToken ct = default);
    Task RemoveAsync(MessageAttachment attachment, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
