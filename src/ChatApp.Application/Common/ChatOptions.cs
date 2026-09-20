namespace ChatApp.Application.Common;

public sealed class ChatOptions
{
    public const string SectionName = "Chat";
    public int MaxMessageLength { get; set; } = 4000;
    public int MaxGroupMembers { get; set; } = 500;
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 100;
}
