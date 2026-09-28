using AlegacyWebPanel.Modules.Analytics.Contracts;

namespace AlegacyWebPanel.Modules.Analytics.Services;

public interface IAnalyticsService
{
    Task<PlayerSummaryResponse> GetPlayerSummaryAsync(string serverId, int days, CancellationToken cancellationToken);
    Task<DisconnectReportResponse> GetDisconnectReportAsync(string serverId, int days, CancellationToken cancellationToken);
    Task<ActivityHeatmapResponse> GetActivityHeatmapAsync(string serverId, int days, CancellationToken cancellationToken);
    Task<PlayerListResponse> GetPlayerListAsync(string serverId, int days, CancellationToken cancellationToken);
    Task<PlayerProfileResponse> GetPlayerProfileAsync(
        string serverId,
        string playerName,
        int days,
        CancellationToken cancellationToken);
    Task<ServerHealthResponse> GetServerHealthAsync(string serverId, int hours, CancellationToken cancellationToken);
    Task<ConnectionQualityResponse> GetConnectionQualityAsync(string serverId, int hours, CancellationToken cancellationToken);
    Task<PlayerConnectionHistoryResponse> GetPlayerConnectionHistoryAsync(
        string serverId,
        string playerName,
        int hours,
        CancellationToken cancellationToken);
}

public interface IAnalyticsRecorder
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task RecordConnectionsAsync(CancellationToken cancellationToken);
    Task ImportPlayerEventsAsync(CancellationToken cancellationToken);
    Task PruneAsync(CancellationToken cancellationToken);
}
