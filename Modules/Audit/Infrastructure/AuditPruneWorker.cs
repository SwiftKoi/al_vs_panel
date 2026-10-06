using AlegacyWebPanel.Modules.Audit.Configuration;
using AlegacyWebPanel.Modules.Audit.Persistence;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.Audit.Infrastructure;

/// <summary>Deletes entries older than <see cref="AuditOptions.RetentionDays"/>. This is the only way entries are ever removed.</summary>
public sealed class AuditPruneWorker(
    IAuditRepository repository,
    IOptions<AuditOptions> options,
    TimeProvider timeProvider,
    ILogger<AuditPruneWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var interval = TimeSpan.FromMinutes(settings.PruneIntervalMinutes);
        using var timer = new PeriodicTimer(interval, timeProvider);

        do
        {
            try
            {
                var cutoff = timeProvider.GetUtcNow().AddDays(-settings.RetentionDays);
                var deleted = await repository.PruneAsync(cutoff, stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Pruned {Deleted} audit entries older than {Days} days", deleted, settings.RetentionDays);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Audit retention pruning failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
