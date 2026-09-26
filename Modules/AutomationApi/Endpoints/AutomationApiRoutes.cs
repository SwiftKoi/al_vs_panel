using AlegacyWebPanel.Modules.AutomationApi.Configuration;
using AlegacyWebPanel.Modules.AutomationApi.Infrastructure;
using AlegacyWebPanel.Modules.ServerManagement.Contracts;
using AlegacyWebPanel.Modules.ServerManagement.Services;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.AutomationApi.Endpoints;

public static class AutomationApiRoutes
{
    public static IEndpointRouteBuilder MapAutomationApiModule(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider
            .GetRequiredService<IOptions<AutomationApiOptions>>().Value;

        if (!options.Enabled)
        {
            return endpoints;
        }

        var group = endpoints.MapGroup("/api/v1")
            .RequireAuthorization(ApiKeyAuthenticationDefaults.Policy);

        group.MapGet("/servers", AutomationApiEndpoints.ListServersAsync);
        group.MapGet("/servers/{serverId}/status", AutomationApiEndpoints.ServerStatusAsync);
        group.MapGet("/servers/{serverId}/files/{root}", AutomationApiEndpoints.ListFilesAsync);
        group.MapGet("/servers/{serverId}/files/{root}/download", AutomationApiEndpoints.DownloadFileAsync);
        group.MapPost("/servers/{serverId}/files/{root}/upload", AutomationApiEndpoints.UploadFileAsync);
        group.MapPost("/servers/{serverId}/start", (string serverId, IServerManagementService service, CancellationToken token) =>
                AutomationApiEndpoints.LifecycleAsync(serverId, ServerLifecycleAction.Start, service, token));
        group.MapPost("/servers/{serverId}/stop", (string serverId, IServerManagementService service, CancellationToken token) =>
                AutomationApiEndpoints.LifecycleAsync(serverId, ServerLifecycleAction.Stop, service, token));
        group.MapPost("/servers/{serverId}/restart", (string serverId, IServerManagementService service, CancellationToken token) =>
                AutomationApiEndpoints.LifecycleAsync(serverId, ServerLifecycleAction.Restart, service, token));

        return endpoints;
    }
}
