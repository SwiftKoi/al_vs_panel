using AlegacyWebPanel.Modules.FileManager.Configuration;
using AlegacyWebPanel.Modules.FileManager.Persistence;
using AlegacyWebPanel.Modules.FileManager.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AlegacyWebPanel.Modules.FileManager.Infrastructure;

public static class FileManagerModule
{
    public static IServiceCollection AddFileManagerModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<FileManagerOptions>()
            .Bind(configuration.GetSection(FileManagerOptions.SectionName))
            .Validate(options => options.MaximumFileSizeBytes > 0, "MaximumFileSizeBytes must be positive.")
            .Validate(options => options.MaximumTextFileSizeBytes > 0, "MaximumTextFileSizeBytes must be positive.")
            .Validate(options => options.MaximumArchiveSizeBytes > 0, "MaximumArchiveSizeBytes must be positive.")
            .Validate(options => options.MaximumListingEntries > 0, "MaximumListingEntries must be positive.")
            .Validate(options => options.MaximumConcurrentOperations > 0, "MaximumConcurrentOperations must be positive.")
            .Validate(options => options.Instances != null, "Instances configuration is missing.")
            .Validate(options => 
            {
                foreach (var (instanceId, instance) in options.Instances)
                {
                    if (instance.Roots == null) return false;
                    foreach (var (rootId, root) in instance.Roots)
                    {
                        if (string.IsNullOrWhiteSpace(root.DisplayName)) return false;
                        if (string.IsNullOrWhiteSpace(root.Path)) return false;
                        if (string.IsNullOrWhiteSpace(root.Operation)) return false;
                    }
                }
                return true;
            }, "Instance root configuration must be valid.")
            .ValidateOnStart();

        services.PostConfigure<FileManagerOptions>(options =>
        {
            var workspacePath = configuration["WorkspacePath"];
            if (string.IsNullOrEmpty(workspacePath)) return;

            foreach (var instance in options.Instances.Values)
            {
                foreach (var root in instance.Roots.Values)
                {
                    if (root.Path.Contains("%WorkspacePath%"))
                    {
                        root.Path = root.Path.Replace("%WorkspacePath%", workspacePath);
                    }
                }
            }
        });


        services.AddSingleton<IBackgroundOperationTracker, BackgroundOperationTracker>();
        services.AddScoped<IFileRepository, RemoteFileRepository>();
        services.AddScoped<IFileManagerService, FileManagerService>();
        return services;
    }
}
