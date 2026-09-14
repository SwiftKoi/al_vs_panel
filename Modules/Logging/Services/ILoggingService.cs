using AlegacyWebPanel.Modules.Logging.Contracts;

namespace AlegacyWebPanel.Modules.Logging.Services;

public interface ILoggingService
{
    Task<LogPageResult> QueryAsync(LogQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListSourcesAsync(CancellationToken cancellationToken);

    Task<LogLevelCounts> GetLevelCountsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken);

    Task<long> DeleteAsync(int? olderThanDays, CancellationToken cancellationToken);
}
