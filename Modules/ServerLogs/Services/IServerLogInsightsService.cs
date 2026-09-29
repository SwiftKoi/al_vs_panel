using AlegacyWebPanel.Modules.ServerLogs.Contracts;

namespace AlegacyWebPanel.Modules.ServerLogs.Services;

public interface IServerLogInsightsService
{
    Task<PlayersResponse> PlayersAsync(string serverId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
    Task<PlayerActivityResponse> PlayerActivityAsync(string serverId, string player, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
    Task<LocationResponse> LocationAsync(string serverId, int x, int? y, int z, int? radius, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
    Task<BootsResponse> BootsAsync(string serverId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
    Task<SuggestionsResponse> SuggestAsync(string serverId, string key, string? prefix, string? context, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
    Task<ProblemSummaryResponse> ProblemSummaryAsync(string serverId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedSearchDto>> SavedSearchesAsync(string userId, string serverId, CancellationToken cancellationToken);
    Task<SavedSearchDto> SaveSearchAsync(string userId, string serverId, SaveSearchRequest request, CancellationToken cancellationToken);
    Task DeleteSavedSearchAsync(string userId, string serverId, long id, CancellationToken cancellationToken);
}
