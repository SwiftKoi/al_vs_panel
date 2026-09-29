using AlegacyWebPanel.Core.Authorization;
using AlegacyWebPanel.Core.Endpoints;
using AlegacyWebPanel.Modules.ServerManagement.Contracts;

namespace AlegacyWebPanel.Modules.ServerManagement.Endpoints;

public static class ServerManagementRoutes
{
    public static IEndpointRouteBuilder MapServerManagementModule(this IEndpointRouteBuilder endpoints)
    {
        // Policies on a group and its endpoints are combined, so moderator routes get their own group.
        var staff = endpoints.MapGroup("/api/servers").RequireAuthorization(PanelPolicies.Staff);
        staff.MapGet("/", ServerManagementEndpoints.ListAsync);
        staff.MapGet("/{serverId}/status", ServerManagementEndpoints.StatusAsync);
        staff.MapGet("/{serverId}/metrics", ServerManagementEndpoints.MetricsAsync);
        staff.MapGet("/{serverId}/connections", ServerManagementEndpoints.ConnectionsAsync);
        staff.MapPost("/{serverId}/actions/gamemode", ServerManagementEndpoints.SetGameModeAsync).RequireAntiforgery();
        staff.MapPost("/{serverId}/actions/teleport", ServerManagementEndpoints.TeleportAsync).RequireAntiforgery();
        staff.MapPost("/{serverId}/actions/warn", ServerManagementEndpoints.WarnAsync).RequireAntiforgery();
        staff.MapPost("/{serverId}/actions/kick", ServerManagementEndpoints.KickAsync).RequireAntiforgery();
        staff.MapPost("/{serverId}/actions/ban", ServerManagementEndpoints.BanAsync).RequireAntiforgery();
        staff.MapPost("/{serverId}/actions/hardban", ServerManagementEndpoints.HardBanAsync).RequireAntiforgery();
        staff.MapPost("/{serverId}/actions/landclaim", ServerManagementEndpoints.LandClaimAsync).RequireAntiforgery();
        staff.MapPost("/{serverId}/actions/allowcharselonce", ServerManagementEndpoints.AllowClassReselectAsync).RequireAntiforgery();
        staff.MapPost("/{serverId}/actions/unban", ServerManagementEndpoints.UnbanAsync).RequireAntiforgery();

        var group = endpoints.MapGroup("/api/servers").RequireAuthorization();
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
