using ChatApp.Application.Abstractions;

namespace ChatApp.Application.Abstractions;

public sealed record AttachmentDownload(Stream Content, string FileName, string ContentType);

public interface IAttachmentService
{
    /// <returns>Metadata of the stored, not-yet-attached upload.</returns>
    Task<MessageAttachmentDto> UploadAsync(Guid userId, string fileName, Stream content, long declaredLength, CancellationToken ct = default);

    /// <summary>Authorized download: owner while unattached, conversation member once attached.</summary>
    Task<AttachmentDownload> GetDownloadAsync(Guid userId, Guid attachmentId, CancellationToken ct = default);

    /// <summary>Owner-only, unattached uploads only. Attached files live and die with their message.</summary>
    Task DeleteAsync(Guid userId, Guid attachmentId, CancellationToken ct = default);
}
