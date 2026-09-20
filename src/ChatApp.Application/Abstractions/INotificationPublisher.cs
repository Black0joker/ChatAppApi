namespace ChatApp.Application.Abstractions;

/// <summary>
/// Real-time fan-out port, adapted in the Api layer with SignalR's IHubContext.
/// Lets application services notify online users without depending on SignalR.
/// </summary>
public interface INotificationPublisher
{
    Task PublishToUserAsync(Guid userId, string method, object payload, CancellationToken ct = default);
}
