using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.Analytics.Configuration;
using AlegacyWebPanel.Modules.Analytics.Contracts;
using AlegacyWebPanel.Modules.Analytics.Persistence;
using AlegacyWebPanel.Modules.RemoteOperations.Services;
using AlegacyWebPanel.Modules.ServerManagement.Services;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.Analytics.Services;

public sealed class AnalyticsRecorder(
    IAnalyticsRepository repository,
    IServerManagementService servers,
    IRemoteOperationsService remoteOperations,
    IOptions<AnalyticsOptions> options,
    TimeProvider time,
    ILogger<AnalyticsRecorder> logger) : IAnalyticsRecorder
{
    public Task InitializeAsync(CancellationToken cancellationToken) =>
        repository.EnsureCreatedAsync(cancellationToken);

    public async Task RecordConnectionsAsync(CancellationToken cancellationToken)
    {
        // One timestamp per round so concurrent-player counts group exactly.
        var now = time.GetUtcNow().UtcDateTime;
        var sampledAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));

        foreach (var server in await servers.ListAsync(cancellationToken))
        {
            try
            {
                var snapshot = await servers.GetConnectionsAsync(server.Id, cancellationToken);
                var samples = snapshot.Connections
                    .Select(connection => new ConnectionSample(
                        server.Id,
                        sampledAt,
                        connection.PlayerName,
                        connection.RemoteAddress,
                        connection.RemotePort,
                        connection.RttMs,
                        connection.RttVarianceMs,
                        connection.RetransmitPercent,
                        connection.RetransmitsTotal,
                        connection.SendQueueBytes,
                        connection.LastReceiveMs,
                        connection.BytesSent,
                        connection.BytesReceived))
                    .ToArray();
                await repository.AddSamplesAsync(samples, cancellationToken);
            }
            catch (DomainException)
            {
                // The server is offline or has no connections operation; nothing to sample.
            }

            try
            {
                var metrics = await servers.GetMetricsAsync(server.Id, cancellationToken);
                await repository.AddMetricSampleAsync(new ServerMetricSample(
                    server.Id,
                    sampledAt,
                    metrics.CpuPercent,
                    metrics.MemoryPercent,
                    MemorySizeParser.ToBytes(metrics.MemoryUsage)), cancellationToken);
            }
            catch (DomainException)
            {
                // The server is offline or has no metrics operation; nothing to sample.
            }
        }
    }

    public async Task ImportPlayerEventsAsync(CancellationToken cancellationToken)
    {
        foreach (var (serverId, source) in options.Value.Servers)
        {
            if (string.IsNullOrWhiteSpace(source.PlayerEventsOperation))
            {
                continue;
            }

            try
            {
                var result = await remoteOperations.ExecuteAsync(source.PlayerEventsOperation, [], cancellationToken);
                if (result.ExitStatus != 0)
                {
                    logger.LogWarning("Player event import failed for server {ServerId}", serverId);
                    continue;
                }

                var batch = PlayerEventParser.Parse(serverId, result.StandardOutput);
                var added = await repository.AddEventsAsync(batch, cancellationToken);
                if (added > 0)
                {
                    logger.LogInformation("Imported {Count} player events for server {ServerId}", added, serverId);
                }
            }
            catch (DomainException)
            {
                logger.LogWarning("Player event operation is unavailable for server {ServerId}", serverId);
            }
        }
    }

    public Task PruneAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return repository.PruneAsync(
            now.AddDays(-options.Value.SampleRetentionDays),
            now.AddDays(-options.Value.EventRetentionDays),
            cancellationToken);
    }
}
