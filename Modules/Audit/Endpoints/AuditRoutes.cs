namespace AlegacyWebPanel.Modules.Audit.Endpoints;

public static class AuditRoutes
{
    public static IEndpointRouteBuilder MapAuditModule(this IEndpointRouteBuilder endpoints)
    {
        // Admin-only (the default policy). There is deliberately no write or delete route: entries are
        // created by the audited endpoints and removed only by retention pruning.
        var group = endpoints.MapGroup("/api/audit").RequireAuthorization();

        group.MapGet("/", AuditEndpoints.QueryAsync);
        group.MapGet("/facets", AuditEndpoints.FacetsAsync);

        return endpoints;
    }
}
