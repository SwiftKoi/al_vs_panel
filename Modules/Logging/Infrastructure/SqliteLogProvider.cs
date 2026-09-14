using AlegacyWebPanel.Modules.Logging.Configuration;
using AlegacyWebPanel.Modules.Logging.Contracts;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.Logging.Infrastructure;

public sealed class SqliteLogProvider(
    IOptions<LogStoreOptions> options,
    Channel<LogEntry> channel,
    LogDropCounter dropCounter) : ILoggerProvider
{
    private readonly LogLevel _minimumLevel = Enum.Parse<LogLevel>(options.Value.MinimumLevel, ignoreCase: true);
    private readonly int _structuredStateMaxBytes = options.Value.StructuredStateMaxBytes;

    public ILogger CreateLogger(string categoryName) =>
        new SqliteLogWriter(categoryName, MinimumLevelFor(categoryName), _structuredStateMaxBytes, channel, dropCounter);

    public void Dispose()
    {
    }

    private LogLevel MinimumLevelFor(string categoryName)
    {
        // EF Core logs every executed command, connection open and context
        // initialization at Debug/Information. Persisting those would create a
        // feedback loop: each log-store write would itself be logged and written
        // again. Keep EF Core categories visible only from Warning upward so
        // genuine database failures are still captured.
        if (categoryName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
        {
            return (LogLevel)Math.Max((int)_minimumLevel, (int)LogLevel.Warning);
        }

        return _minimumLevel;
    }
}
