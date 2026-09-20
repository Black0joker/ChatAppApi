using System.Text.Json;
using ChatApp.Application.Abstractions;
using ChatApp.Application.Common;
using ChatApp.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatApp.Application.Outbox;

public sealed record OutboxBatchResult(int Processed, int Failed, int DeadLettered, int CleanedUp);

/// <summary>
/// Dispatches due outbox events with exponential-backoff retries (PLAN §12).
/// Runs inside a scope per batch; safe to overlap (rows are claimed by marking
/// processed — at-least-once delivery, clients dedupe by message id).
/// </summary>
public sealed class OutboxProcessor(
    IOutboxRepository outbox,
    INotificationPublisher publisher,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    public static string SerializeMessageReceived(Message message)
        => JsonSerializer.Serialize(new MessageReceivedEvent(
            message.Id, message.ConversationId, message.SenderId,
            message.IsDeleted ? null : message.Content,
            message.MessageType, message.CreatedAt, message.ReplyToMessageId), Json);

    public async Task<OutboxBatchResult> ProcessBatchAsync(CancellationToken ct = default)
    {
        var maxAttempts = options.Value.MaxAttempts <= 0 ? 10 : options.Value.MaxAttempts;
        var pending = await outbox.GetPendingAsync(
            options.Value.BatchSize <= 0 ? 50 : options.Value.BatchSize, ct);

        var processed = 0; var failed = 0; var deadLettered = 0;
        foreach (var @event in pending)
        {
            try
            {
                await publisher.PublishToConversationAsync(@event.ConversationId, @event.Type, @event.Payload, ct);
                @event.MarkProcessed();
                processed++;
            }
            catch (Exception ex)
            {
                // Attempts is pre-increment inside RecordFailure; backoff grows 2s, 4s, 8s… capped.
                var nextDelay = NextDelay(@event.Attempts + 1);
                @event.RecordFailure(Truncate(ex.Message), DateTimeOffset.UtcNow.Add(nextDelay));
                if (@event.Attempts >= maxAttempts)
                {
                    @event.MarkProcessed(); // dead-letter: Error kept for ops, row retained till cleanup
                    deadLettered++;
                    logger.LogError(ex, "Outbox event dead-lettered. EventId={EventId} Type={Type}", @event.Id, @event.Type);
                }
                else
                {
                    failed++;
                    logger.LogWarning(ex, "Outbox publish failed (attempt {Attempts}). EventId={EventId}", @event.Attempts, @event.Id);
                }
            }
        }

        if (pending.Count > 0)
            await outbox.SaveChangesAsync(ct);

        var retention = options.Value.RetentionHours <= 0 ? 24 : options.Value.RetentionHours;
        var cleaned = await outbox.DeleteProcessedBeforeAsync(DateTimeOffset.UtcNow.AddHours(-retention), ct);

        return new OutboxBatchResult(processed, failed, deadLettered, cleaned);
    }

    internal static TimeSpan NextDelay(int attempt)
    {
        var seconds = 1 << Math.Min(attempt, 8); // 2, 4, 8 … 256
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
    }

    private static string Truncate(string message)
        => message.Length <= 2000 ? message : message[..2000];
}
