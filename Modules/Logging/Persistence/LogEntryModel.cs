namespace AlegacyWebPanel.Modules.Logging.Persistence;

public sealed class LogEntryModel
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string Level { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public int EventId { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? StateJson { get; set; }

    public string? ExceptionType { get; set; }

    public string? ExceptionMessage { get; set; }

    public string? StackTrace { get; set; }
}
