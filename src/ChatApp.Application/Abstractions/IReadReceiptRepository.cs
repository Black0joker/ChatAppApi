using ChatApp.Domain.Entities;

namespace ChatApp.Application.Abstractions;

public interface IReadReceiptRepository
{
    Task<MessageReadReceipt?> GetAsync(Guid messageId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<MessageReadReceipt>> GetForMessagesAsync(IEnumerable<Guid> messageIds, CancellationToken ct = default);
    Task AddAsync(MessageReadReceipt receipt, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
