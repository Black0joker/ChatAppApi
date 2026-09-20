using ChatApp.Application.Common;
using ChatApp.Application.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatApp.Infrastructure.Outbox;

/// <summary>
/// Polls the transactional outbox and dispatches due events (PLAN §39).
/// A scope per tick; overlapping runs are safe (at-least-once, idempotent consumers).
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(
            options.Value.PollIntervalMs <= 0 ? 1000 : options.Value.PollIntervalMs);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                var result = await processor.ProcessBatchAsync(stoppingToken);
                if (result.Processed > 0 || result.Failed > 0 || result.DeadLettered > 0)
                    logger.LogInformation(
                        "Outbox batch: {Processed} dispatched, {Failed} retrying, {DeadLettered} dead-lettered, {CleanedUp} cleaned.",
                        result.Processed, result.Failed, result.DeadLettered, result.CleanedUp);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox dispatch tick failed; will retry on the next tick.");
            }
        }
    }
}
