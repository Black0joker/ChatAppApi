namespace ChatApp.Application.Abstractions;

/// <summary>
/// Mobile push provider abstraction (PLAN §19). Keeps Application independent of
/// FCM/APNs specifics; the Phase 11 implementation logs, production swaps it.
/// </summary>
public interface IPushNotificationService
{
    Task PushAsync(Guid userId, string title, string body, IReadOnlyDictionary<string, string>? data, CancellationToken ct = default);
}
