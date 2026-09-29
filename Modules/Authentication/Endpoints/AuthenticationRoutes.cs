using AlegacyWebPanel.Core.Authorization;
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
        group.MapGet("/session", AuthenticationEndpoints.SessionAsync).RequireAuthorization(PanelPolicies.SignedIn);
        group.MapPost("/refresh", AuthenticationEndpoints.RefreshAsync).RequireAuthorization(PanelPolicies.SignedIn).RequireAntiforgery();
        group.MapPost("/login", AuthenticationEndpoints.LoginAsync).RequireAntiforgery();
        group.MapPost("/login/2fa", AuthenticationEndpoints.LoginTwoFactorAsync).RequireAntiforgery();
        group.MapPost("/logout", AuthenticationEndpoints.LogoutAsync).RequireAuthorization(PanelPolicies.SignedIn).RequireAntiforgery();
        group.MapGet("/login-logs", AuthenticationEndpoints.RecentLoginsAsync).RequireAuthorization();

        // Own-account routes: any panel role, plus antiforgery.
        group.MapPost("/password", AuthenticationEndpoints.ChangePasswordAsync).RequireAuthorization(PanelPolicies.Staff).RequireAntiforgery();
        group.MapGet("/2fa/status", AuthenticationEndpoints.TwoFactorStatusAsync).RequireAuthorization(PanelPolicies.Staff);
        group.MapPost("/2fa/setup", AuthenticationEndpoints.GetTwoFactorSetupAsync).RequireAuthorization(PanelPolicies.Staff).RequireAntiforgery();
        group.MapPost("/2fa/enable", AuthenticationEndpoints.EnableTwoFactorAsync).RequireAuthorization(PanelPolicies.Staff).RequireAntiforgery();
        group.MapPost("/2fa/disable", AuthenticationEndpoints.DisableTwoFactorAsync).RequireAuthorization(PanelPolicies.Staff).RequireAntiforgery();

        return endpoints;
    }
}
