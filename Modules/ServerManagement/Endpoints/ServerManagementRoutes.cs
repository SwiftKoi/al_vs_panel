using AlegacyWebPanel.Core.Endpoints;
using AlegacyWebPanel.Modules.ServerManagement.Contracts;

namespace AlegacyWebPanel.Modules.ServerManagement.Endpoints;

public static class ServerManagementRoutes
{
    public static IEndpointRouteBuilder MapServerManagementModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/servers").RequireAuthorization();

        group.MapGet("/", ServerManagementEndpoints.ListAsync);
        group.MapGet("/{serverId}/status", ServerManagementEndpoints.StatusAsync);
        group.MapGet("/{serverId}/metrics", ServerManagementEndpoints.MetricsAsync);
        group.MapGet("/{serverId}/logs", ServerManagementEndpoints.LogsAsync);
        group.MapPost("/{serverId}/start", (string serverId, Services.IServerManagementService service, CancellationToken token) =>
                ServerManagementEndpoints.LifecycleAsync(serverId, ServerLifecycleAction.Start, service, token))
            .RequireAntiforgery();
        group.MapPost("/{serverId}/stop", (string serverId, Services.IServerManagementService service, CancellationToken token) =>
                ServerManagementEndpoints.LifecycleAsync(serverId, ServerLifecycleAction.Stop, service, token))
            .RequireAntiforgery();
        group.MapPost("/{serverId}/restart", (string serverId, Services.IServerManagementService service, CancellationToken token) =>
                ServerManagementEndpoints.LifecycleAsync(serverId, ServerLifecycleAction.Restart, service, token))
            .RequireAntiforgery();
        group.MapPost("/{serverId}/commands", ServerManagementEndpoints.CommandAsync).RequireAntiforgery();

        return endpoints;
    }
}
