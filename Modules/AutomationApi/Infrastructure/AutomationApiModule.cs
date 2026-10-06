using AlegacyWebPanel.Modules.AutomationApi.Configuration;
using AlegacyWebPanel.Modules.AutomationApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using AlegacyWebPanel.Core.Abstractions;

namespace AlegacyWebPanel.Modules.AutomationApi.Infrastructure;

public static class AutomationApiModule
{
    public static IServiceCollection AddAutomationApiModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AutomationApiOptions>()
            .Bind(configuration.GetSection(AutomationApiOptions.SectionName))
            .Validate(
                settings => !settings.Enabled ||
                    (!string.IsNullOrWhiteSpace(settings.KeyFile) && File.Exists(settings.KeyFile)),
                "AutomationApi:KeyFile must reference an existing secret file when the API is enabled.")
            .Validate(
                settings => !settings.Enabled ||
                    string.IsNullOrWhiteSpace(settings.ModsKeyFile) ||
                    File.Exists(settings.ModsKeyFile),
                "AutomationApi:ModsKeyFile must reference an existing secret file when it is set.")
            .ValidateOnStart();

        // Registered without a default scheme so the browser cookie scheme
        // registered by the Authentication module remains the application default.
        services.AddAuthentication()
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.Scheme,
                _ => { })
            .AddScheme<ApiKeyAuthenticationOptions, ModsApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.ModsScheme,
                _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(ApiKeyAuthenticationDefaults.Policy, policy =>
            {
                policy.AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme);
                policy.RequireAuthenticatedUser();
            });
            options.AddPolicy(ApiKeyAuthenticationDefaults.ModsPolicy, policy =>
            {
                policy.AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.ModsScheme);
                policy.RequireAuthenticatedUser();
            });
        });

        services.AddSingleton<IApiKeyValidator>(provider => new ApiKeyValidator(
            provider.GetRequiredService<ISecretReader>(),
            provider.GetRequiredService<IOptions<AutomationApiOptions>>(),
            provider.GetRequiredService<ILogger<ApiKeyValidator>>(),
            ApiKeyScope.Automation));
        services.AddKeyedSingleton<IApiKeyValidator>(ApiKeyScope.Mods, (provider, _) => new ApiKeyValidator(
            provider.GetRequiredService<ISecretReader>(),
            provider.GetRequiredService<IOptions<AutomationApiOptions>>(),
            provider.GetRequiredService<ILogger<ApiKeyValidator>>(),
            ApiKeyScope.Mods));

        return services;
    }
}
