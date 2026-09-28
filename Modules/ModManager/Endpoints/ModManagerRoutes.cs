using AlegacyWebPanel.Core.Endpoints;

namespace AlegacyWebPanel.Modules.ModManager.Endpoints;

public static class ModManagerRoutes
{
    public static IEndpointRouteBuilder MapModManagerModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/servers/{serverId}/mods").RequireAuthorization();

        group.MapGet("", ModManagerEndpoints.OverviewAsync);
        group.MapGet("/update", ModManagerEndpoints.CurrentJob);
        group.MapPost("/update", ModManagerEndpoints.StartUpdateAsync).RequireAntiforgery();
        group.MapPost("/rollback", ModManagerEndpoints.RollbackAsync).RequireAntiforgery();
        group.MapGet("/{modId}", ModManagerEndpoints.DetailAsync);
        group.MapPut("/{modId}/pin", ModManagerEndpoints.SetPinnedAsync).RequireAntiforgery();

        return endpoints;
    }
}
