using AlegacyWebPanel.Core.Auditing;
using AlegacyWebPanel.Core.Endpoints;

namespace AlegacyWebPanel.Modules.ModManager.Endpoints;

public static class ModManagerRoutes
{
    public static IEndpointRouteBuilder MapModManagerModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/servers/{serverId}/mods").RequireAuthorization();

        group.MapGet("", ModManagerEndpoints.OverviewAsync);
        group.MapGet("/update", ModManagerEndpoints.CurrentJob);
        group.MapPost("/update", ModManagerEndpoints.StartUpdateAsync).RequireAntiforgery().Audited(AuditCategories.Mods, "update");
        group.MapPost("/rollback", ModManagerEndpoints.RollbackAsync).RequireAntiforgery().Audited(AuditCategories.Mods, "rollback");
        group.MapGet("/{modId}", ModManagerEndpoints.DetailAsync);
        group.MapPut("/{modId}/pin", ModManagerEndpoints.SetPinnedAsync).RequireAntiforgery().Audited(AuditCategories.Mods, "pin");

        return endpoints;
    }
}
