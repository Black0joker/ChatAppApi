using ChatApp.Application.Abstractions;

namespace ChatApp.Application.Abstractions;

public interface IMessageService
{
    Task<MessageDto> SendAsync(Guid userId, Guid conversationId, SendMessageRequest request, CancellationToken ct = default);
    Task<MessageHistoryDto> GetHistoryAsync(Guid userId, Guid conversationId, Guid? before, Guid? after, int? limit, CancellationToken ct = default);
    Task<MessageDto> EditAsync(Guid userId, Guid messageId, EditMessageRequest request, CancellationToken ct = default);
    /// <summary>Soft-deletes and returns the deleted message (needed for broadcast routing).</summary>
    Task<MessageDto> DeleteAsync(Guid userId, Guid messageId, CancellationToken ct = default);
    /// <summary>
    /// Records a read. <see cref="MarkAsReadResult.IsNewActivity"/> is false for
    /// self-marks (no row, no broadcast) — callers should only broadcast on new activity.
    /// </summary>
    Task<MarkAsReadResult> MarkAsReadAsync(Guid userId, Guid conversationId, Guid messageId, CancellationToken ct = default);
}
