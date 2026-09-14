using AlegacyWebPanel.Core.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AlegacyWebPanel.Modules.Logging.Endpoints;

public static class LoggingRoutes
{
    public static IEndpointRouteBuilder MapLoggingModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/logs").RequireAuthorization();

        group.MapGet("/", LoggingEndpoints.QueryAsync);
        group.MapGet("/sources", LoggingEndpoints.SourcesAsync);
        group.MapGet("/summary", LoggingEndpoints.SummaryAsync);
        group.MapGet("/errors", LoggingEndpoints.ErrorsAsync);
        group.MapDelete("/", LoggingEndpoints.DeleteAsync).RequireAntiforgery();

        return endpoints;
    }
}
