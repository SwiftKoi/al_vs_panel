using AlegacyWebPanel.Modules.Logging.Contracts;
using AlegacyWebPanel.Modules.Logging.Exceptions;
using AlegacyWebPanel.Modules.Logging.Persistence;

namespace AlegacyWebPanel.Modules.Logging.Services;

public sealed class LoggingService(ILogRepository repository) : ILoggingService
{
    private const int MaximumPageSize = 1000;
    private const int MaximumFilterLength = 200;

    public async Task<LogPageResult> QueryAsync(
        LogQuery query,
        CancellationToken cancellationToken)
    {
        ValidateQuery(query);
        var result = await repository.QueryAsync(query, cancellationToken);
        return new LogPageResult(
            result.Entries.Select(ToDto).ToArray(),
            result.Total,
            query.Offset,
            query.Limit);
    }

    public async Task<IReadOnlyList<string>> ListSourcesAsync(CancellationToken cancellationToken) =>
        await repository.ListSourcesAsync(cancellationToken);

    public async Task<LogLevelCounts> GetLevelCountsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (from is { } start && to is { } end && start > end)
        {
            throw new InvalidLogQueryException("The 'from' timestamp must not be after 'to'.");
        }

        return await repository.CountByLevelAsync(from, to, cancellationToken);
    }

    public async Task<long> DeleteAsync(int? olderThanDays, CancellationToken cancellationToken)
    {
        if (olderThanDays is { } days && days < 1)
        {
            throw new InvalidLogQueryException("The retention filter must be at least one day.");
        }

        return await repository.DeleteAsync(olderThanDays, cancellationToken);
    }

    private static void ValidateQuery(LogQuery query)
    {
        if (query.Limit is < 1 or > MaximumPageSize)
        {
            throw new InvalidLogQueryException($"The page size must be between 1 and {MaximumPageSize}.");
        }

        if (query.Offset < 0)
        {
            throw new InvalidLogQueryException("The offset must not be negative.");
        }

        if (query.From is { } from && query.To is { } to && from > to)
        {
            throw new InvalidLogQueryException("The 'from' timestamp must not be after 'to'.");
        }

        if (query.Source is { Length: > MaximumFilterLength })
        {
            throw new InvalidLogQueryException($"The source filter must be at most {MaximumFilterLength} characters.");
        }

        if (query.Search is { Length: > MaximumFilterLength })
        {
            throw new InvalidLogQueryException($"The search text must be at most {MaximumFilterLength} characters.");
        }
    }

    private static LogEventDto ToDto(LogEntry entry) =>
        new(
            entry.Id,
            entry.TimestampUtc,
            entry.Level,
            entry.Category,
            entry.EventId,
            entry.Message,
            entry.StateJson,
            entry.ExceptionType,
            entry.ExceptionMessage,
            entry.StackTrace);
}
