namespace ChatApp.Application.Common;

public sealed class ChatOptions
{
    public const string SectionName = "Chat";
    public int MaxMessageLength { get; set; } = 4000;
    public int MaxGroupMembers { get; set; } = 500;
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 100;
    public long MaxAttachmentSize { get; set; } = 10 * 1024 * 1024;
    public int MaxAttachmentsPerMessage { get; set; } = 5;
    /// <summary>Allowed upload extensions (lowercase, with dot). Content is sniffed — never trusted.</summary>
    public string[] AllowedAttachmentExtensions { get; set; } = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf", ".txt"];
}
