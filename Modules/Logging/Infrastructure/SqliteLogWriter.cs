using System.Text.Json;
using AlegacyWebPanel.Core.Security;
using AlegacyWebPanel.Modules.Logging.Contracts;

namespace AlegacyWebPanel.Modules.Logging.Infrastructure;

public sealed class SqliteLogWriter(
    string categoryName,
    LogLevel minimumLevel,
    int structuredStateMaxBytes,
    Channel<LogEntry> channel,
    LogDropCounter dropCounter) : ILogger
{
    private const int MaxMessageLength = 8000;
    private const int MaxStackTraceLength = 16000;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None && logLevel >= minimumLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        var entry = new LogEntry(
            Id: 0,
            TimestampUtc: DateTimeOffset.UtcNow,
            Level: logLevel.ToString(),
            Category: categoryName,
            EventId: eventId.Id,
            Message: Truncate(message ?? string.Empty, MaxMessageLength) ?? string.Empty,
            StateJson: SerializeState(state, structuredStateMaxBytes),
            ExceptionType: exception?.GetType().FullName,
            ExceptionMessage: Truncate(exception?.Message, MaxMessageLength),
            StackTrace: exception is null ? null : Truncate(exception.ToString(), MaxStackTraceLength));

        if (!channel.Writer.TryWrite(entry))
        {
            dropCounter.Increment();
        }
    }

    private static string? SerializeState<TState>(TState state, int maxBytes)
    {
        if (state is null or string)
        {
            return null;
        }

        if (state is not IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            return null;
        }

        var values = new Dictionary<string, object?>();
        foreach (var pair in pairs)
        {
            if (pair.Key.Length == 0 || pair.Key == "{OriginalFormat}")
            {
                continue;
            }

            values[pair.Key] = pair.Value is SecretValue secret ? secret.ToString() : pair.Value;
        }

        if (values.Count == 0)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(values);
        if (json.Length <= maxBytes)
        {
            return json;
        }

        foreach (var key in values.Keys.ToArray())
        {
            if (json.Length <= maxBytes)
            {
                break;
            }

            values[key] = "[TRUNCATED]";
            json = JsonSerializer.Serialize(values);
        }

        return json.Length <= maxBytes ? json : null;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength];
}
