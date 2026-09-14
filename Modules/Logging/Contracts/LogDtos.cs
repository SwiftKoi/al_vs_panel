namespace AlegacyWebPanel.Modules.Logging.Contracts;

public sealed record LogQuery(
    LogLevel? MinimumLevel = null,
    string? Source = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Search = null,
    int Limit = 100,
    int Offset = 0);

public sealed record LogQueryResult(IReadOnlyList<LogEntry> Entries, long Total);

public sealed record LogEventDto(
    long Id,
    DateTimeOffset TimestampUtc,
    string Level,
    string Category,
    int EventId,
    string Message,
    string? StateJson,
    string? ExceptionType,
    string? ExceptionMessage,
    string? StackTrace);

public sealed record LogPageResult(
    IReadOnlyList<LogEventDto> Items,
    long Total,
    int Offset,
    int Limit);

public sealed record LogLevelCounts(
    long Debug,
    long Information,
    long Warning,
    long Error,
    long Critical);

public sealed record LogDeleteResult(long Deleted);
