using AlegacyWebPanel.Modules.ServerManagement.Configuration;
using AlegacyWebPanel.Modules.ServerManagement.Persistence;
using AlegacyWebPanel.Modules.ServerManagement.Services;

namespace AlegacyWebPanel.Modules.ServerManagement.Infrastructure;

public static class ServerManagementModule
{
    public static IServiceCollection AddServerManagementModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ServerManagementOptions>()
            .Bind(configuration.GetSection(ServerManagementOptions.SectionName))
            .Validate(options => options.MaximumCommandLength > 0, "MaximumCommandLength must be positive.")
            .Validate(options => options.Instances.All(instance => !string.IsNullOrWhiteSpace(instance.Id)),
                "Every server instance must have an ID.")
            .Validate(options => options.Instances.Select(instance => instance.Id).Distinct(StringComparer.Ordinal).Count() ==
                                 options.Instances.Count,
                "Server instance IDs must be unique.")
            .ValidateOnStart();

        services.AddSingleton<IServerRepository, ConfigurationServerRepository>();
        services.AddSingleton<IServerLifecycleOperationGuard, ServerLifecycleOperationGuard>();
        services.AddScoped<IServerManagementService, ServerManagementService>();
        return services;
    }
}
