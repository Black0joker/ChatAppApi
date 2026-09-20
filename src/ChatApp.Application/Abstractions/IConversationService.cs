using ChatApp.Application.Abstractions;

namespace ChatApp.Application.Abstractions;

public interface IConversationService
{
    Task<ConversationDto> CreateDirectAsync(Guid userId, CreateDirectRequest request, CancellationToken ct = default);
    Task<ConversationDto> CreateGroupAsync(Guid userId, CreateGroupRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ConversationDto>> GetMineAsync(Guid userId, CancellationToken ct = default);
    Task<ConversationDto> GetByIdAsync(Guid userId, Guid conversationId, CancellationToken ct = default);
    Task<ConversationDto> AddMemberAsync(Guid userId, Guid conversationId, AddMemberRequest request, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid userId, Guid conversationId, Guid targetUserId, CancellationToken ct = default);
}
