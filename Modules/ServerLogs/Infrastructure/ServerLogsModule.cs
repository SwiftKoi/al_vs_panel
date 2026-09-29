using AlegacyWebPanel.Modules.ServerLogs.Configuration;
using AlegacyWebPanel.Modules.ServerLogs.Persistence;
using AlegacyWebPanel.Modules.ServerLogs.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AlegacyWebPanel.Modules.ServerLogs.Infrastructure;

public static class ServerLogsModule
{
    public static IServiceCollection AddServerLogsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ServerLogsOptions>()
            .Bind(configuration.GetSection(ServerLogsOptions.SectionName))
            .Validate(ValidateOptions, "ServerLogs configuration is invalid.")
            .ValidateOnStart();

        var databasePath = configuration.GetSection(ServerLogsOptions.SectionName)["DatabasePath"]
                           ?? new ServerLogsOptions().DatabasePath;
        var databaseDirectory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(databaseDirectory))
        {
            Directory.CreateDirectory(databaseDirectory);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(_ => new SqliteLogIndexRepository(databasePath));
        services.AddSingleton<ILogIndexRepository>(provider => provider.GetRequiredService<SqliteLogIndexRepository>());
        services.AddSingleton<ILogInsightsRepository>(provider => provider.GetRequiredService<SqliteLogIndexRepository>());
        services.AddSingleton<LogIndexState>();
        services.AddScoped<ILogSourceRepository, RemoteLogSourceRepository>();
        services.AddScoped<ILogIndexer, LogIndexer>();
        services.AddScoped<IServerLogService, ServerLogService>();
        services.AddScoped<IServerLogInsightsService, ServerLogInsightsService>();
        services.AddHostedService<ServerLogsWorker>();

        return services;
    }

    private static bool ValidateOptions(ServerLogsOptions options) =>
        !string.IsNullOrWhiteSpace(options.DatabasePath) &&
        options.IndexIntervalSeconds >= 5 &&
        options.ReadChunkBytes is >= 64 * 1024 and <= 16 * 1024 * 1024 &&
        options.MaximumBytesPerPass >= options.ReadChunkBytes &&
        options.RetentionDays.Values.All(days => days > 0) &&
        options.MaximumPageSize > 0 &&
        options.MaximumContextLines > 0 &&
        options.MaximumExportRows > 0 &&
        options.Servers.Values.All(server => !string.IsNullOrWhiteSpace(server.Operation));
}
