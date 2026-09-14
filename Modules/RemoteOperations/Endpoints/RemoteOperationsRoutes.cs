using AlegacyWebPanel.Core.Endpoints;
using AlegacyWebPanel.Modules.RemoteOperations.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace AlegacyWebPanel.Modules.RemoteOperations.Endpoints;

public static class RemoteOperationsRoutes
{
    public static IEndpointRouteBuilder MapRemoteOperationsModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/remote/{operation}", async (
            string operation,
            IRemoteOperationsService service,
            CancellationToken cancellationToken) => RemoteOperationsEndpoints.ExecuteAsync(operation, service, cancellationToken))
            .RequireAuthorization()
            .RequireAntiforgery();

        return endpoints;
    }
}
