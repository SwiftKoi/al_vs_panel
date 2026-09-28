using AlegacyWebPanel.Modules.Analytics.Configuration;
using AlegacyWebPanel.Modules.Analytics.Persistence;
using AlegacyWebPanel.Modules.Analytics.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;

namespace AlegacyWebPanel.Modules.Analytics.Infrastructure;

public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AnalyticsOptions>()
            .Bind(configuration.GetSection(AnalyticsOptions.SectionName))
            .Validate(ValidateOptions, "Analytics configuration is invalid.")
            .ValidateOnStart();

        var databasePath = configuration.GetSection(AnalyticsOptions.SectionName)["DatabasePath"]
                           ?? new AnalyticsOptions().DatabasePath;
        var databaseDirectory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(databaseDirectory))
        {
            Directory.CreateDirectory(databaseDirectory);
        }

        services.AddDbContextFactory<AnalyticsDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath};Cache=Shared"));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAnalyticsRepository, SqliteAnalyticsRepository>();
        services.AddSingleton<IProxyClassifier, ProxyClassifier>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<IAnalyticsRecorder, AnalyticsRecorder>();
        services.AddHostedService<AnalyticsWorker>();

        return services;
    }

    private static bool ValidateOptions(AnalyticsOptions options) =>
        !string.IsNullOrWhiteSpace(options.DatabasePath) &&
        options.SampleIntervalSeconds >= 10 &&
        options.EventImportIntervalMinutes > 0 &&
        options.SampleRetentionDays > 0 &&
        options.EventRetentionDays > 0 &&
        options.ProxyAddresses.All(IsValidAddressOrRange);

    private static bool IsValidAddressOrRange(string entry) =>
        entry.Contains('/') ? IPNetwork.TryParse(entry.Trim(), out _) : IPAddress.TryParse(entry.Trim(), out _);
}
