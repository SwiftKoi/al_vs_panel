namespace AlegacyWebPanel.Modules.Logging.Contracts;

public sealed record LogEntry(
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
