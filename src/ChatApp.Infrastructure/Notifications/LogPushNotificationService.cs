using ChatApp.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ChatApp.Infrastructure.Notifications;

/// <summary>
/// Phase 11 push provider: logs instead of calling FCM/APNs. Proves the fan-out
/// path end to end; a real provider replaces this one class.
/// </summary>
public sealed class LogPushNotificationService(ILogger<LogPushNotificationService> logger) : IPushNotificationService
{
    public Task PushAsync(Guid userId, string title, string body, IReadOnlyDictionary<string, string>? data, CancellationToken ct = default)
    {
        logger.LogInformation("Push to {UserId}: {Title} — {Body}", userId, title, body);
        return Task.CompletedTask;
    }
}
