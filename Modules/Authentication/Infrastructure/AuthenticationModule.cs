using AlegacyWebPanel.Modules.Authentication.Configuration;
using AlegacyWebPanel.Modules.Authentication.Persistence;
using AlegacyWebPanel.Modules.Authentication.Services;
using AlegacyWebPanel.Modules.Users.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.Authentication.Infrastructure;

public static class AuthenticationModule
{
    public static IServiceCollection AddAuthenticationModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<AuthenticationOptions>()
            .Bind(configuration.GetSection(AuthenticationOptions.SectionName))
            .Validate(options => options.CookieLifetimeHours > 0, "Cookie lifetime must be positive")
            .ValidateOnStart();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();
        services.AddAuthorization();

        services.ConfigureApplicationCookie(options =>
        {
            var settings = configuration.GetSection(AuthenticationOptions.SectionName).Get<AuthenticationOptions>() ?? new();
            options.Cookie.Name = settings.CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(settings.CookieLifetimeHours);
            options.SlidingExpiration = settings.SlidingExpiration;
            options.LoginPath = "/auth/login";
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddHttpContextAccessor();
        services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        services.AddScoped<IAuthenticationRepository, IdentityAuthenticationRepository>();
        services.AddScoped<ITrustedIpRepository, EfTrustedIpRepository>();
        services.AddScoped<ILoginEventRepository>(sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var options = sp.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
            return new EfLoginEventRepository(db, TimeSpan.FromDays(options.LoginLog.MaxRetainedDays));
        });
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddScoped<ILoginLogService, LoginLogService>();
        services.AddScoped<IAuthenticationSession, IdentityAuthenticationSession>();

        return services;
    }
}
