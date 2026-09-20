using ChatApp.Application.Abstractions;
using ChatApp.Application.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatApp.Infrastructure.Redis;

/// <summary>
/// Periodic eviction of lapsed connections (PLAN §14: TTL/heartbeat so abandoned
/// connections can't leave stale presence). Runs on every API instance; the sweep
/// itself is idempotent, so overlapping runs are harmless. Creates a scope per
/// tick — a singleton must never capture scoped services.
/// </summary>
public sealed class PresenceSweepService(
    IServiceScopeFactory scopes,
    IOptions<PresenceOptions> options,
    ILogger<PresenceSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(
            options.Value.SweepIntervalSeconds <= 0 ? 20 : options.Value.SweepIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        // Let the app finish starting before the first pass.
        await Task.Delay(interval, stoppingToken).ConfigureAwait(false);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var presence = scope.ServiceProvider.GetRequiredService<IPresenceService>();
                var evicted = await presence.SweepStaleConnectionsAsync(stoppingToken);
                if (evicted > 0)
                    logger.LogInformation("Presence sweep evicted {Count} stale connection(s).", evicted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Presence sweep failed; will retry on the next tick.");
            }
        }
    }
}
