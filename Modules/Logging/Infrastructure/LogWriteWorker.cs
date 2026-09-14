using AlegacyWebPanel.Modules.Logging.Configuration;
using AlegacyWebPanel.Modules.Logging.Contracts;
using AlegacyWebPanel.Modules.Logging.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.Logging.Infrastructure;

public class LogWriteWorker(
    Channel<LogEntry> channel,
    ILogRepository repository,
    IDbContextFactory<LogDbContext> dbFactory,
    IOptions<LogStoreOptions> options,
    ILogger fallbackLogger,
    LogDropCounter dropCounter) : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
            await db.Database.EnsureCreatedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await FlushRemainingAsync();
            return;
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            fallbackLogger.LogError(exception, "The log store could not be initialized.");
            return;
        }

        var pruneInterval = TimeSpan.FromMinutes(settings.PruneIntervalMinutes);
        var lastPruneAt = DateTimeOffset.UtcNow;
        var lastReportedDrops = 0L;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainBatchAsync(settings.FlushBatchSize, stoppingToken);

                var now = DateTimeOffset.UtcNow;
                if (now - lastPruneAt >= pruneInterval)
                {
                    await repository.PruneAsync(settings.MaximumRetainedDays, settings.MaximumRetainedEntries, stoppingToken);
                    lastPruneAt = now;
                }

                var currentDrops = dropCounter.Dropped;
                if (currentDrops > lastReportedDrops)
                {
                    fallbackLogger.LogWarning(
                        "The log store buffer is full; {Dropped} log entries were dropped.",
                        currentDrops - lastReportedDrops);
                    lastReportedDrops = currentDrops;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                fallbackLogger.LogError(exception, "The log store write loop failed; recent entries were not persisted.");
            }
        }

        await FlushRemainingAsync();
    }

    private async Task DrainBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var batch = new List<LogEntry>(batchSize);
        var flushAt = DateTimeOffset.UtcNow + FlushInterval;

        while (batch.Count < batchSize)
        {
            var remaining = flushAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(remaining);

            try
            {
                if (!await channel.Reader.WaitToReadAsync(timeout.Token))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            while (batch.Count < batchSize && channel.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
            }
        }

        if (batch.Count > 0)
        {
            await repository.WriteAsync(
                batch,
                cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken);
        }
    }

    private async Task FlushRemainingAsync()
    {
        try
        {
            var remaining = new List<LogEntry>();
            while (channel.Reader.TryRead(out var entry))
            {
                remaining.Add(entry);
            }

            if (remaining.Count > 0)
            {
                await repository.WriteAsync(remaining, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            fallbackLogger.LogError(exception, "Failed to flush remaining log entries on shutdown.");
        }
    }
}
