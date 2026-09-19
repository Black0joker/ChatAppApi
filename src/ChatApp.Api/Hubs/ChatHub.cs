using ChatApp.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Api.Hubs;

/// <summary>
/// Phase 2: proves authenticated SignalR works (Context.User available,
/// never trust client-provided userId). Full messaging arrives in Phase 5.
/// </summary>
[Authorize]
public sealed class ChatHub(ILogger<ChatHub> logger) : Hub
{
    public override Task OnConnectedAsync()
    {
        var userId = Context.User?.GetUserId();
        logger.LogInformation("SignalR connected. ConnectionId={ConnectionId} UserId={UserId}",
            Context.ConnectionId, userId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("SignalR disconnected. ConnectionId={ConnectionId} Error={Error}",
            Context.ConnectionId, exception?.Message);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Health-check round trip for authenticated clients.</summary>
    public Task<string> Ping()
    {
        var username = Context.User?.Identity?.Name ?? "unknown";
        return Task.FromResult($"pong:{username}:{Context.ConnectionId}");
    }

    /// <summary>Returns the server-derived identity — proves userId comes from the token.</summary>
    public Task<object> WhoAmI()
    {
        var user = Context.User!;
        return Task.FromResult<object>(new
        {
            userId = user.GetUserId(),
            username = user.Identity?.Name
        });
    }
}
