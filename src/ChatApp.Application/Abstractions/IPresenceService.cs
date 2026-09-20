namespace ChatApp.Application.Abstractions;

public interface IPresenceService
{
    /// <returns>True when the user just came online (first connection).</returns>
    Task<bool> UserConnectedAsync(Guid userId, string connectionId, CancellationToken ct = default);

    /// <returns>True when the user just went offline (last connection removed).</returns>
    Task<bool> UserDisconnectedAsync(Guid userId, string connectionId, CancellationToken ct = default);

    Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default);
}
