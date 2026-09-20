namespace ChatApp.Application.Common;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";
    public int PollIntervalMs { get; set; } = 1000;
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 10;
    public int RetentionHours { get; set; } = 24;
}
