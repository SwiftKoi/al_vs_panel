using AlegacyWebPanel.Modules.ServerLogs.Configuration;
using AlegacyWebPanel.Modules.ServerLogs.Persistence;
using AlegacyWebPanel.Modules.ServerLogs.Services;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.ServerLogs.Infrastructure;

/// <summary>Indexes every configured server's logs on a timer and prunes entries past retention.</summary>
public sealed class ServerLogsWorker(
    IServiceScopeFactory scopeFactory,
    ILogIndexRepository index,
    LogIndexState state,
    IOptions<ServerLogsOptions> options,
    TimeProvider time,
    ILogger<ServerLogsWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan BackfillPause = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || settings.Servers.Count == 0)
        {
            return;
        }

        try
        {
            await index.InitializeAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to initialize the server log index");
            return;
        }

        var interval = TimeSpan.FromSeconds(settings.IndexIntervalSeconds);
        var lastPrune = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var more = false;
            var prune = time.GetUtcNow() - lastPrune >= PruneInterval;
            foreach (var (serverId, server) in settings.Servers)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var indexer = scope.ServiceProvider.GetRequiredService<ILogIndexer>();
                    var result = await indexer.IndexAsync(serverId, server, stoppingToken);
                    more |= result.MoreAvailable;
                    if (result.BytesRead > 0)
                    {
                        logger.LogDebug("Indexed {Entries} log entries ({Bytes} bytes) of {Server}", result.Entries, result.BytesRead, serverId);
                    }

                    if (prune)
                    {
                        await indexer.PruneAsync(serverId, stoppingToken);
                    }

                    state.Record(serverId, time.GetUtcNow(), null);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Failed to index server logs of {Server}", serverId);
                    state.Record(serverId, time.GetUtcNow(), exception.Message);
                }
            }

            if (prune)
            {
                lastPrune = time.GetUtcNow();
            }

            try
            {
                // While backfilling, continue soon; the per-pass byte limit already spreads the load.
                await Task.Delay(more ? BackfillPause : interval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
