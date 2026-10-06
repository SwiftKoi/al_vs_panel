using AlegacyWebPanel.Core.Auditing;
using AlegacyWebPanel.Modules.Audit.Configuration;
using AlegacyWebPanel.Modules.Audit.Persistence;
using AlegacyWebPanel.Modules.Audit.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AlegacyWebPanel.Modules.Audit.Infrastructure;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuditOptions>()
            .Bind(configuration.GetSection(AuditOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DatabasePath)
                           && options.RetentionDays > 0
                           && options.PruneIntervalMinutes > 0
                           && options.MaximumPageSize > 0,
                "AuditTrail configuration is invalid.")
            .ValidateOnStart();

        var databasePath = configuration.GetSection(AuditOptions.SectionName)["DatabasePath"]
                           ?? new AuditOptions().DatabasePath;
        var databaseDirectory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(databaseDirectory))
        {
            Directory.CreateDirectory(databaseDirectory);
        }

        services.AddDbContextFactory<AuditDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath};Cache=Shared"));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAuditRepository, SqliteAuditRepository>();
        services.AddSingleton<IAuditTrail, AuditTrailService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddHostedService<AuditPruneWorker>();

        return services;
    }
}
