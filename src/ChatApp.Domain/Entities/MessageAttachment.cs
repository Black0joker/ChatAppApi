namespace ChatApp.Domain.Entities;

/// <summary>
/// File metadata (PLAN §18). Two-step flow: the row is created at upload time
/// (owned by the uploader, unattached), then linked to a message on send after
/// the server verifies ownership. File bytes live in object/file storage —
/// never in SQL Server.
/// </summary>
public sealed class MessageAttachment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? MessageId { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long Size { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public Message? Message { get; private set; }

    private MessageAttachment() { } // EF Core

    public MessageAttachment(Guid uploadedByUserId, string fileName, string contentType, long size, string storageKey)
    {
        Id = Guid.NewGuid();
        UploadedByUserId = uploadedByUserId;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        StorageKey = storageKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsAttached => MessageId.HasValue;

    public void AttachTo(Guid messageId)
    {
        if (IsAttached)
            throw new InvalidOperationException("Attachment is already linked to a message.");
        MessageId = messageId;
    }
}
