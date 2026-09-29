using AlegacyWebPanel.Modules.ServerLogs.Contracts;

namespace AlegacyWebPanel.Modules.ServerLogs.Persistence;

public sealed record LogPlaceFilter(int X, int? Y, int Z, int Radius);

public sealed record BootMarker(long TimestampMs, string Message, string? Extra);

/// <summary>Aggregations over the indexed entries: players, locations, server starts, and saved searches.</summary>
public interface ILogInsightsRepository
{
    Task<IReadOnlyList<PlayerSummaryDto>> PlayersAsync(string serverId, long fromMs, long toMs, int limit, CancellationToken cancellationToken);
    Task<(long? FirstMs, long? LastMs)> PlayerSeenAsync(string serverId, string player, long fromMs, long toMs, CancellationToken cancellationToken);
    Task<IReadOnlyList<FacetValue>> ActionCountsAsync(string serverId, string? player, LogPlaceFilter? place, long fromMs, long toMs, CancellationToken cancellationToken);
    Task<IReadOnlyList<ItemTotalDto>> ItemTotalsAsync(string serverId, string? player, LogPlaceFilter? place, string action, long fromMs, long toMs, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<LogEntryDto>> PlayerEntriesAsync(string serverId, string player, IReadOnlyList<string> actions, long fromMs, long toMs, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlaceDto>> PlacesAsync(string serverId, string player, long fromMs, long toMs, int gridSize, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<DayActivityDto>> DailyActivityAsync(string serverId, string player, long fromMs, long toMs, CancellationToken cancellationToken);
    Task<IReadOnlyList<(string Player, string Action, long Count, long FirstMs, long LastMs)>> LocationActivityAsync(string serverId, LogPlaceFilter place, long fromMs, long toMs, CancellationToken cancellationToken);

    /// <summary>Most frequent values for an autocomplete key starting with <paramref name="prefix"/>, within the entries matching <paramref name="context"/>.</summary>
    Task<IReadOnlyList<FacetValue>> SuggestAsync(LogFilter context, string key, string prefix, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<BootMarker>> BootMarkersAsync(string serverId, long fromMs, long toMs, CancellationToken cancellationToken);
    Task<(long Warnings, long Errors)> ProblemCountsAsync(string serverId, long fromMs, long toMs, CancellationToken cancellationToken);
    Task<(long Errors, long Warnings)> NewSignatureCountsAsync(string serverId, long sinceMs, CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedSearchDto>> SavedSearchesAsync(string userId, string serverId, CancellationToken cancellationToken);
    Task<SavedSearchDto> AddSavedSearchAsync(string userId, string serverId, string name, string query, string range, long createdMs, CancellationToken cancellationToken);
    Task<bool> DeleteSavedSearchAsync(string userId, string serverId, long id, CancellationToken cancellationToken);
    Task<int> CountSavedSearchesAsync(string userId, string serverId, CancellationToken cancellationToken);
}
