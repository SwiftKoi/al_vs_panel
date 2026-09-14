using AlegacyWebPanel.Core.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace AlegacyWebPanel.Modules.Authentication.Endpoints;

public static class AuthenticationRoutes
{
    public static IEndpointRouteBuilder MapAuthenticationModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth");

        group.MapGet("/csrf", AuthenticationEndpoints.CsrfAsync);
        group.MapGet("/session", AuthenticationEndpoints.SessionAsync).RequireAuthorization();
        group.MapPost("/refresh", AuthenticationEndpoints.RefreshAsync).RequireAuthorization().RequireAntiforgery();
        group.MapPost("/login", AuthenticationEndpoints.LoginAsync).RequireAntiforgery();
        group.MapPost("/login/2fa", AuthenticationEndpoints.LoginTwoFactorAsync).RequireAntiforgery();
        group.MapPost("/logout", AuthenticationEndpoints.LogoutAsync).RequireAuthorization().RequireAntiforgery();
        group.MapGet("/login-logs", AuthenticationEndpoints.RecentLoginsAsync).RequireAuthorization();

        // 2FA configuration routes (require auth and antiforgery)
        group.MapPost("/2fa/setup", AuthenticationEndpoints.GetTwoFactorSetupAsync).RequireAuthorization().RequireAntiforgery();
        group.MapPost("/2fa/enable", AuthenticationEndpoints.EnableTwoFactorAsync).RequireAuthorization().RequireAntiforgery();
        group.MapPost("/2fa/disable", AuthenticationEndpoints.DisableTwoFactorAsync).RequireAuthorization().RequireAntiforgery();

        return endpoints;
    }
}
