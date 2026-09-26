using AlegacyWebPanel.Modules.AutomationApi.Configuration;
using AlegacyWebPanel.Modules.AutomationApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

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
            .ValidateOnStart();

        // Registered without a default scheme so the browser cookie scheme
        // registered by the Authentication module remains the application default.
        services.AddAuthentication()
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.Scheme,
                _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(ApiKeyAuthenticationDefaults.Policy, policy =>
            {
                policy.AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme);
                policy.RequireAuthenticatedUser();
            });
        });

        services.AddSingleton<IApiKeyValidator, ApiKeyValidator>();

        return services;
    }
}
