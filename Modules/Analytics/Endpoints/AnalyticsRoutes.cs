namespace AlegacyWebPanel.Modules.Analytics.Endpoints;

public static class AnalyticsRoutes
{
    public static IEndpointRouteBuilder MapAnalyticsModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/analytics").RequireAuthorization();

        group.MapGet("/{serverId}/players", AnalyticsEndpoints.PlayerSummaryAsync);
        group.MapGet("/{serverId}/players/heatmap", AnalyticsEndpoints.ActivityHeatmapAsync);
        group.MapGet("/{serverId}/players/list", AnalyticsEndpoints.PlayerListAsync);
        group.MapGet("/{serverId}/players/list/{playerName}", AnalyticsEndpoints.PlayerProfileAsync);
        group.MapGet("/{serverId}/disconnects", AnalyticsEndpoints.DisconnectReportAsync);
        group.MapGet("/{serverId}/health", AnalyticsEndpoints.ServerHealthAsync);
        group.MapGet("/{serverId}/connection-quality", AnalyticsEndpoints.ConnectionQualityAsync);
        group.MapGet("/{serverId}/connection-quality/{playerName}", AnalyticsEndpoints.PlayerConnectionHistoryAsync);

        return endpoints;
    }
}
