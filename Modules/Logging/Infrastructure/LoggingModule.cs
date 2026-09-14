using AlegacyWebPanel.Modules.Logging.Configuration;
using AlegacyWebPanel.Modules.Logging.Contracts;
using AlegacyWebPanel.Modules.Logging.Persistence;
using AlegacyWebPanel.Modules.Logging.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.Logging.Infrastructure;

public static class LoggingModule
{
    public static IServiceCollection AddLoggingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<LogStoreOptions>()
            .Bind(configuration.GetSection(LogStoreOptions.SectionName))
            .Validate(ValidateOptions, "LogStore configuration is invalid.")
            .ValidateOnStart();

        var databasePath = configuration.GetSection(LogStoreOptions.SectionName)["DatabasePath"]
                           ?? "/var/lib/alegacy/data/logs.db";
        var databaseDirectory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(databaseDirectory))
        {
            Directory.CreateDirectory(databaseDirectory);
        }

        services.AddDbContextFactory<LogDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath};Cache=Shared"));

        services.AddSingleton<LogDropCounter>();
        services.AddSingleton<Channel<LogEntry>>(static provider =>
            Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(
                provider.GetRequiredService<IOptions<LogStoreOptions>>().Value.BufferCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            }));

        services.AddSingleton<ILogger>(static _ =>
            LoggerFactory.Create(static builder => builder.AddConsole())
                .CreateLogger("AlegacyWebPanel.Logging"));

        services.AddSingleton<ILoggerProvider, SqliteLogProvider>();
        services.AddSingleton<ILogRepository, SqliteLogRepository>();
        services.AddScoped<ILoggingService, LoggingService>();
        services.AddHostedService<LogWriteWorker>();

        return services;
    }

    private static bool ValidateOptions(LogStoreOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            return false;
        }

        if (options.MaximumRetainedDays <= 0 ||
            options.MaximumRetainedEntries <= 0 ||
            options.BufferCapacity <= 0 ||
            options.FlushBatchSize <= 0 ||
            options.PruneIntervalMinutes <= 0 ||
            options.StructuredStateMaxBytes <= 0)
        {
            return false;
        }

        return Enum.TryParse<LogLevel>(options.MinimumLevel, ignoreCase: true, out _);
    }
}
