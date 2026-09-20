using ChatApp.Application.Abstractions;

namespace ChatApp.Application.Abstractions;

public interface IMessageService
{
    Task<MessageDto> SendAsync(Guid userId, Guid conversationId, SendMessageRequest request, CancellationToken ct = default);
    Task<MessageHistoryDto> GetHistoryAsync(Guid userId, Guid conversationId, Guid? before, Guid? after, int? limit, CancellationToken ct = default);
    Task<MessageDto> EditAsync(Guid userId, Guid messageId, EditMessageRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid messageId, CancellationToken ct = default);
}
