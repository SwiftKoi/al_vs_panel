using AlegacyWebPanel.Modules.Logging.Contracts;
using AlegacyWebPanel.Modules.Logging.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AlegacyWebPanel.Modules.Logging.Persistence;

public sealed class SqliteLogRepository(IDbContextFactory<LogDbContext> dbFactory) : ILogRepository
{
    public async Task WriteAsync(
        IReadOnlyList<LogEntry> entries,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            async (db, token) =>
            {
                db.LogEntries.AddRange(entries.Select(ToModel));
                await db.SaveChangesAsync(token);
                return true;
            },
            cancellationToken);
    }

    public async Task PruneAsync(
        int maximumRetainedDays,
        long maximumRetainedEntries,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            async (db, token) =>
            {
                var cutoff = DateTime.UtcNow.AddDays(-maximumRetainedDays);
                await db.LogEntries
                    .Where(entry => entry.TimestampUtc < cutoff)
                    .ExecuteDeleteAsync(token);

                var total = await db.LogEntries.LongCountAsync(token);
                if (total <= maximumRetainedEntries)
                {
                    return true;
                }

                var oldestKept = await db.LogEntries
                    .OrderByDescending(entry => entry.TimestampUtc)
                    .ThenByDescending(entry => entry.Id)
                    .Skip((int)maximumRetainedEntries - 1)
                    .Select(entry => entry.TimestampUtc)
                    .FirstOrDefaultAsync(token);

                if (oldestKept != default)
                {
                    await db.LogEntries
                        .Where(entry => entry.TimestampUtc < oldestKept)
                        .ExecuteDeleteAsync(token);
                }

                return true;
            },
            cancellationToken);
    }

    public async Task<LogQueryResult> QueryAsync(
        LogQuery query,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            async (db, token) =>
            {
                var filtered = ApplyQuery(db.LogEntries, query);
                var total = await filtered.LongCountAsync(token);

                var entries = await filtered
                    .OrderByDescending(entry => entry.TimestampUtc)
                    .ThenByDescending(entry => entry.Id)
                    .Skip(query.Offset)
                    .Take(query.Limit)
                    .Select(entry => new LogEntry(
                        entry.Id,
                        new DateTimeOffset(entry.TimestampUtc, TimeSpan.Zero),
                        entry.Level,
                        entry.Category,
                        entry.EventId,
                        entry.Message,
                        entry.StateJson,
                        entry.ExceptionType,
                        entry.ExceptionMessage,
                        entry.StackTrace))
                    .ToListAsync(token);

                return new LogQueryResult(entries, total);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListSourcesAsync(CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            async (db, token) => await db.LogEntries
                .Select(entry => entry.Category)
                .Distinct()
                .OrderBy(category => category)
                .ToListAsync(token),
            cancellationToken);
    }

    public async Task<LogLevelCounts> CountByLevelAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            async (db, token) =>
            {
                IQueryable<LogEntryModel> filtered = db.LogEntries;
                if (from is { } start)
                {
                    filtered = filtered.Where(entry => entry.TimestampUtc >= start.UtcDateTime);
                }

                if (to is { } end)
                {
                    filtered = filtered.Where(entry => entry.TimestampUtc <= end.UtcDateTime);
                }

                var grouped = await filtered
                    .GroupBy(entry => entry.Level)
                    .Select(group => new { Level = group.Key, Count = group.LongCount() })
                    .ToListAsync(token);

                var counts = grouped.ToDictionary(group => group.Level, group => group.Count);
                return new LogLevelCounts(
                    Debug: counts.GetValueOrDefault("Debug"),
                    Information: counts.GetValueOrDefault("Information"),
                    Warning: counts.GetValueOrDefault("Warning"),
                    Error: counts.GetValueOrDefault("Error"),
                    Critical: counts.GetValueOrDefault("Critical"));
            },
            cancellationToken);
    }

    public async Task<long> DeleteAsync(int? olderThanDays, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            async (db, token) =>
            {
                IQueryable<LogEntryModel> filtered = db.LogEntries;
                if (olderThanDays is { } days)
                {
                    var cutoff = DateTimeOffset.UtcNow.AddDays(-days).UtcDateTime;
                    filtered = filtered.Where(entry => entry.TimestampUtc < cutoff);
                }

                return await filtered.ExecuteDeleteAsync(token);
            },
            cancellationToken);
    }

    private static IQueryable<LogEntryModel> ApplyQuery(IQueryable<LogEntryModel> source, LogQuery query)
    {
        var filtered = source;
        if (query.MinimumLevel is { } level)
        {
            var allowed = LevelsAtOrAbove(level).ToArray();
            filtered = filtered.Where(entry => allowed.Contains(entry.Level));
        }

        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            filtered = filtered.Where(entry => entry.Category.Contains(query.Source));
        }

        if (query.From is { } from)
        {
            filtered = filtered.Where(entry => entry.TimestampUtc >= from.UtcDateTime);
        }

        if (query.To is { } to)
        {
            filtered = filtered.Where(entry => entry.TimestampUtc <= to.UtcDateTime);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            filtered = filtered.Where(entry => entry.Message.Contains(query.Search));
        }

        return filtered;
    }

    private static IEnumerable<string> LevelsAtOrAbove(LogLevel minimumLevel) =>
        Enum.GetValues<LogLevel>()
            .Where(level => level != LogLevel.None && level >= minimumLevel)
            .Select(level => level.ToString());

    private static LogEntryModel ToModel(LogEntry entry) =>
        new()
        {
            TimestampUtc = entry.TimestampUtc.UtcDateTime,
            Level = entry.Level,
            Category = entry.Category,
            EventId = entry.EventId,
            Message = entry.Message,
            StateJson = entry.StateJson,
            ExceptionType = entry.ExceptionType,
            ExceptionMessage = entry.ExceptionMessage,
            StackTrace = entry.StackTrace
        };

    private async Task<T> ExecuteAsync<T>(
        Func<LogDbContext, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return await operation(db, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LogStoreUnavailableException("The log store is temporarily unavailable.", exception);
        }
    }
}
