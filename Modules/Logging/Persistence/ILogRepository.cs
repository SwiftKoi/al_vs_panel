using AlegacyWebPanel.Modules.Logging.Contracts;

namespace AlegacyWebPanel.Modules.Logging.Persistence;

public interface ILogRepository
{
    Task WriteAsync(IReadOnlyList<LogEntry> entries, CancellationToken cancellationToken);

    Task PruneAsync(int maximumRetainedDays, long maximumRetainedEntries, CancellationToken cancellationToken);

    Task<LogQueryResult> QueryAsync(LogQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListSourcesAsync(CancellationToken cancellationToken);

    Task<LogLevelCounts> CountByLevelAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken);

    Task<long> DeleteAsync(int? olderThanDays, CancellationToken cancellationToken);
}
