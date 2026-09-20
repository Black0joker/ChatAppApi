using ChatApp.Application.Abstractions;
using ChatApp.Application.Common;
using ChatApp.Application.Common.Exceptions;
using ChatApp.Domain.Entities;
using Microsoft.Extensions.Options;

namespace ChatApp.Application.Attachments;

/// <summary>
/// Upload first, attach on send (PLAN §18). The server verifies ownership before
/// an upload can be linked to a message, and validates bytes server-side —
/// client claims about type/size are never trusted.
/// </summary>
public sealed class AttachmentService(
    IAttachmentRepository attachments,
    IConversationRepository conversations,
    IMessageRepository messages,
    IFileStorage storage,
    IFileSignatureProbe signatureProbe,
    IOptions<ChatOptions> chatOptions) : IAttachmentService
{
    private readonly ChatOptions _chat = chatOptions.Value;

    public async Task<MessageAttachmentDto> UploadAsync(Guid userId, string fileName, Stream content, long declaredLength, CancellationToken ct = default)
    {
        var safeName = Path.GetFileName(fileName?.Trim() ?? string.Empty);
        if (safeName.Length == 0 || safeName.Length > 255)
            throw new ValidationAppException("A file name of 1–255 characters is required.");

        var ext = Path.GetExtension(safeName).ToLowerInvariant();
        if (!_chat.AllowedAttachmentExtensions.Contains(ext))
            throw new ValidationAppException($"File type '{ext}' is not allowed.");

        if (declaredLength <= 0 || declaredLength > _chat.MaxAttachmentSize)
            throw new ValidationAppException($"File must be between 1 and {_chat.MaxAttachmentSize} bytes.");

        // Buffer bounded: never trust declared length, enforce the cap while copying.
        using var buffer = new MemoryStream();
        var remaining = _chat.MaxAttachmentSize + 1;
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            remaining -= read;
            if (remaining < 0)
                throw new ValidationAppException($"File exceeds the {_chat.MaxAttachmentSize} byte limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        if (buffer.Length == 0)
            throw new ValidationAppException("Empty files are not allowed.");

        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 64));
        var detected = signatureProbe.Detect(ext, header)
            ?? throw new ValidationAppException("File content does not match its extension.");

        buffer.Position = 0;
        var stored = await storage.SaveAsync(buffer, ext, ct);

        var attachment = new MessageAttachment(userId, safeName, detected, stored.Size, stored.StorageKey);
        try
        {
            await attachments.AddAsync(attachment, ct);
            await attachments.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(stored.StorageKey, ct); // no orphaned bytes on DB failure
            throw;
        }
        return MessageAttachmentDto.From(attachment);
    }

    public async Task<AttachmentDownload> GetDownloadAsync(Guid userId, Guid attachmentId, CancellationToken ct = default)
    {
        var attachment = await attachments.GetByIdAsync(attachmentId, ct)
            ?? throw new NotFoundAppException("Attachment not found.");

        if (!attachment.IsAttached)
        {
            if (attachment.UploadedByUserId != userId)
                throw new NotFoundAppException("Attachment not found."); // unattached = invisible to others
        }
        else
        {
            var message = await messages.GetByIdAsync(attachment.MessageId!.Value, ct)
                ?? throw new NotFoundAppException("Attachment not found.");
            var conversation = await conversations.GetByIdAsync(message.ConversationId, ct)
                ?? throw new NotFoundAppException("Attachment not found.");
            if (conversation.Members.All(m => m.UserId != userId || !m.IsActive))
                throw new NotFoundAppException("Attachment not found.");
        }

        var stream = await storage.OpenReadAsync(attachment.StorageKey, ct);
        return new AttachmentDownload(stream, attachment.FileName, attachment.ContentType);
    }

    public async Task DeleteAsync(Guid userId, Guid attachmentId, CancellationToken ct = default)
    {
        var attachment = await attachments.GetByIdAsync(attachmentId, ct)
            ?? throw new NotFoundAppException("Attachment not found.");
        if (attachment.UploadedByUserId != userId)
            throw new ForbiddenAppException("Only the uploader can delete an attachment.");
        if (attachment.IsAttached)
            throw new ConflictAppException("Attached files can only be removed with their message.");

        await storage.DeleteAsync(attachment.StorageKey, ct);
        await attachments.RemoveAsync(attachment, ct);
        await attachments.SaveChangesAsync(ct);
    }
}
