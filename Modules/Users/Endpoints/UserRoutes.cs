using AlegacyWebPanel.Core.Auditing;
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
        group.MapPost("/", UserEndpoints.CreateAsync).RequireAntiforgery().Audited(AuditCategories.Users, "create");
        group.MapDelete("/{id}", UserEndpoints.DeleteAsync).RequireAntiforgery().Audited(AuditCategories.Users, "delete");
        group.MapPut("/{id}/role", UserEndpoints.ChangeRoleAsync).RequireAntiforgery().Audited(AuditCategories.Users, "change-role");

        return endpoints;
    }
}
