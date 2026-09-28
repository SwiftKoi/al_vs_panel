using AlegacyWebPanel.Modules.ModManager.Configuration;
using AlegacyWebPanel.Modules.ModManager.Persistence;
using AlegacyWebPanel.Modules.ModManager.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.ModManager.Infrastructure;

public static class ModManagerModule
{
    public static IServiceCollection AddModManagerModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ModManagerOptions>()
            .Bind(configuration.GetSection(ModManagerOptions.SectionName))
            .Validate(ValidateOptions, "ModManager configuration is invalid.")
            .ValidateOnStart();

        var settingsPath = configuration.GetSection(ModManagerOptions.SectionName)["SettingsPath"]
                           ?? "/var/lib/alegacy/data/mod-manager.json";

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient<IModDbRepository, ModDbHttpRepository>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ModManagerOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AlegacyWebPanel-ModManager/1.0");
        });
        services.AddSingleton<IModSettingsRepository>(_ => new JsonModSettingsRepository(settingsPath));
        services.AddSingleton<IChangelogSanitizer, ChangelogSanitizer>();
        services.AddSingleton<IModUpdateJobTracker, ModUpdateJobTracker>();
        services.AddScoped<IModTargetRepository, RemoteModTargetRepository>();
        services.AddScoped<ModUpdateRunner>();
        services.AddScoped<IModManagerService, ModManagerService>();

        return services;
    }

    private static bool ValidateOptions(ModManagerOptions options) =>
        Uri.TryCreate(options.ModDbBaseUrl, UriKind.Absolute, out var baseUri) && baseUri.Scheme == Uri.UriSchemeHttps &&
        options.TrustedDownloadHosts.Count > 0 &&
        options.CacheMinutes > 0 &&
        options.RequestTimeoutSeconds > 0 &&
        options.MaximumApiResponseBytes > 0 &&
        options.MaximumDownloadBytes > 0 &&
        options.MaximumConcurrentRequests > 0;
}
