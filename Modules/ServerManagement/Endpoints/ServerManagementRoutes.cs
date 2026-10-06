using AlegacyWebPanel.Core.Auditing;
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
        staff.MapPost("/{serverId}/actions/gamemode", ServerManagementEndpoints.SetGameModeAsync).RequireAntiforgery().Audited(AuditCategories.Moderation, "gamemode");
        staff.MapPost("/{serverId}/actions/teleport", ServerManagementEndpoints.TeleportAsync).RequireAntiforgery().Audited(AuditCategories.Moderation, "teleport");
        staff.MapPost("/{serverId}/actions/warn", ServerManagementEndpoints.WarnAsync).RequireAntiforgery().Audited(AuditCategories.Moderation, "warn");
        staff.MapPost("/{serverId}/actions/kick", ServerManagementEndpoints.KickAsync).RequireAntiforgery().Audited(AuditCategories.Moderation, "kick");
        staff.MapPost("/{serverId}/actions/ban", ServerManagementEndpoints.BanAsync).RequireAntiforgery().Audited(AuditCategories.Moderation, "ban");
        staff.MapPost("/{serverId}/actions/hardban", ServerManagementEndpoints.HardBanAsync).RequireAntiforgery().Audited(AuditCategories.Moderation, "hardban");
        staff.MapPost("/{serverId}/actions/landclaim", ServerManagementEndpoints.LandClaimAsync).RequireAntiforgery().Audited(AuditCategories.Moderation, "landclaim");
        staff.MapPost("/{serverId}/actions/allowcharselonce", ServerManagementEndpoints.AllowClassReselectAsync).RequireAntiforgery().Audited(AuditCategories.Moderation, "allowcharselonce");
        staff.MapPost("/{serverId}/actions/unban", ServerManagementEndpoints.UnbanAsync).RequireAntiforgery().Audited(AuditCategories.Moderation, "unban");

        var group = endpoints.MapGroup("/api/servers").RequireAuthorization();
        group.MapGet("/{serverId}/logs", ServerManagementEndpoints.LogsAsync);
        group.MapPost("/{serverId}/start", (string serverId, Services.IServerManagementService service, CancellationToken token) =>
                ServerManagementEndpoints.LifecycleAsync(serverId, ServerLifecycleAction.Start, service, token))
            .RequireAntiforgery()
            .Audited(AuditCategories.Server, "start");
        group.MapPost("/{serverId}/stop", (string serverId, Services.IServerManagementService service, CancellationToken token) =>
                ServerManagementEndpoints.LifecycleAsync(serverId, ServerLifecycleAction.Stop, service, token))
            .RequireAntiforgery()
            .Audited(AuditCategories.Server, "stop");
        group.MapPost("/{serverId}/restart", (string serverId, Services.IServerManagementService service, CancellationToken token) =>
                ServerManagementEndpoints.LifecycleAsync(serverId, ServerLifecycleAction.Restart, service, token))
            .RequireAntiforgery()
            .Audited(AuditCategories.Server, "restart");
        group.MapPost("/{serverId}/commands", ServerManagementEndpoints.CommandAsync).RequireAntiforgery().Audited(AuditCategories.Server, "command");

        return endpoints;
    }
}
