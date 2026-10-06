using AlegacyWebPanel.Core.Auditing;
using AlegacyWebPanel.Core.Authorization;
using AlegacyWebPanel.Core.Endpoints;

namespace AlegacyWebPanel.Modules.ServerLogs.Endpoints;

public static class ServerLogsRoutes
{
    public static IEndpointRouteBuilder MapServerLogsModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/servers/{serverId}/server-logs").RequireAuthorization(PanelPolicies.Staff);

        group.MapGet("/status", ServerLogsEndpoints.StatusAsync);
        group.MapGet("/search", ServerLogsEndpoints.SearchAsync);
        group.MapGet("/facets", ServerLogsEndpoints.FacetsAsync);
        group.MapGet("/histogram", ServerLogsEndpoints.HistogramAsync);
        group.MapGet("/export", ServerLogsEndpoints.Export);
        group.MapGet("/entries/{entryId:long}/context", ServerLogsEndpoints.ContextAsync);
        group.MapGet("/signatures", ServerLogsEndpoints.SignaturesAsync);
        group.MapPut("/signatures/{signatureId:long}/mute", ServerLogsEndpoints.SetMutedAsync).RequireAntiforgery().Audited(AuditCategories.Logs, "mute-signature");
        group.MapGet("/suggest", ServerLogsEndpoints.SuggestAsync);
        group.MapGet("/problems/summary", ServerLogsEndpoints.ProblemSummaryAsync);
        group.MapGet("/players", ServerLogsEndpoints.PlayersAsync);
        group.MapGet("/players/{player}/activity", ServerLogsEndpoints.PlayerActivityAsync);
        group.MapGet("/location", ServerLogsEndpoints.LocationAsync);
        group.MapGet("/boots", ServerLogsEndpoints.BootsAsync);
        group.MapGet("/saved-searches", ServerLogsEndpoints.SavedSearchesAsync);
        group.MapPost("/saved-searches", ServerLogsEndpoints.SaveSearchAsync).RequireAntiforgery();
        group.MapDelete("/saved-searches/{id:long}", ServerLogsEndpoints.DeleteSavedSearchAsync).RequireAntiforgery();

        return endpoints;
    }
}
