using AlegacyWebPanel.Core.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AlegacyWebPanel.Modules.Users.Endpoints;

public static class UserRoutes
{
    public static IEndpointRouteBuilder MapUsersModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/users").RequireAuthorization();

        group.MapGet("/", UserEndpoints.ListAsync);
        group.MapPost("/", UserEndpoints.CreateAsync).RequireAntiforgery();
        group.MapDelete("/{id}", UserEndpoints.DeleteAsync).RequireAntiforgery();

        return endpoints;
    }
}
