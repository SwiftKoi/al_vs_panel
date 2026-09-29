using AlegacyWebPanel.Modules.ServerLogs.Configuration;
using AlegacyWebPanel.Modules.ServerLogs.Contracts;
using AlegacyWebPanel.Modules.ServerLogs.Exceptions;
using AlegacyWebPanel.Modules.ServerLogs.Persistence;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.ServerLogs.Services;

public sealed class ServerLogInsightsService(
    ILogIndexRepository index,
    ILogInsightsRepository insights,
    IOptions<ServerLogsOptions> options,
    TimeProvider time) : IServerLogInsightsService
{
    public const int MaximumSavedSearches = 50;
    private const int MaximumLocationRadius = 1000;
    private const int PlayerLimit = 500;
    private const int ItemLimit = 25;
    private const int EntryLimit = 100;
    private const int PlaceGrid = 32;
    private const int SuggestionLimit = 12;
    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(7);
    private static readonly TimeSpan BootRange = TimeSpan.FromDays(30);
    private static readonly string[] Ranges = ["1h", "6h", "24h", "7d", "30d", "all"];

    private readonly ServerLogsOptions _options = options.Value;

    public async Task<PlayersResponse> PlayersAsync(string serverId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var (fromMs, toMs) = Prepare(serverId, from, to, DefaultRange);
        return new PlayersResponse(await insights.PlayersAsync(serverId, fromMs, toMs, PlayerLimit, cancellationToken));
    }

    public async Task<PlayerActivityResponse> PlayerActivityAsync(string serverId, string player, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var (fromMs, toMs) = Prepare(serverId, from, to, DefaultRange);
        if (string.IsNullOrWhiteSpace(player) || player.Length > 80)
        {
            throw new LogQueryException("Invalid player name.");
        }

        var (first, last) = await insights.PlayerSeenAsync(serverId, player, fromMs, toMs, cancellationToken);
        return new PlayerActivityResponse(
            player,
            first is { } f ? DateTimeOffset.FromUnixTimeMilliseconds(f) : null,
            last is { } l ? DateTimeOffset.FromUnixTimeMilliseconds(l) : null,
            await insights.ActionCountsAsync(serverId, player, null, fromMs, toMs, cancellationToken),
            await insights.ItemTotalsAsync(serverId, player, null, "take", fromMs, toMs, ItemLimit, cancellationToken),
            await insights.ItemTotalsAsync(serverId, player, null, "put", fromMs, toMs, ItemLimit, cancellationToken),
            (await insights.ItemTotalsAsync(serverId, player, null, "kill", fromMs, toMs, ItemLimit, cancellationToken))
                .Select(kill => new FacetValue(kill.Item, kill.Events)).ToArray(),
            await insights.PlayerEntriesAsync(serverId, player, ["command"], fromMs, toMs, EntryLimit, cancellationToken),
            await insights.PlayerEntriesAsync(serverId, player, ["death"], fromMs, toMs, EntryLimit, cancellationToken),
            await insights.PlayerEntriesAsync(serverId, player, ["join", "leave"], fromMs, toMs, EntryLimit, cancellationToken),
            await insights.PlacesAsync(serverId, player, fromMs, toMs, PlaceGrid, 10, cancellationToken),
            await insights.DailyActivityAsync(serverId, player, fromMs, toMs, cancellationToken));
    }

    public async Task<LocationResponse> LocationAsync(string serverId, int x, int? y, int z, int? radius, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var (fromMs, toMs) = Prepare(serverId, from, to, DefaultRange);
        var size = radius ?? LogQuery.DefaultNearRadius;
        if (size is < 1 or > MaximumLocationRadius)
        {
            throw new LogQueryException($"The radius must be between 1 and {MaximumLocationRadius}.");
        }

        var place = new LogPlaceFilter(x, y, z, size);
        var rows = await insights.LocationActivityAsync(serverId, place, fromMs, toMs, cancellationToken);
        var players = rows
            .GroupBy(row => row.Player, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LocationPlayerDto(
                group.First().Player,
                group.Sum(row => row.Count),
                group.OrderByDescending(row => row.Count).Select(row => new FacetValue(row.Action, row.Count)).ToArray(),
                DateTimeOffset.FromUnixTimeMilliseconds(group.Min(row => row.FirstMs)),
                DateTimeOffset.FromUnixTimeMilliseconds(group.Max(row => row.LastMs))))
            .OrderByDescending(player => player.LastSeen)
            .ToArray();

        return new LocationResponse(
            x, y, z, size, players.Sum(player => player.Count), players,
            await insights.ItemTotalsAsync(serverId, null, place, "take", fromMs, toMs, ItemLimit, cancellationToken),
            await insights.ItemTotalsAsync(serverId, null, place, "put", fromMs, toMs, ItemLimit, cancellationToken));
    }

    public async Task<BootsResponse> BootsAsync(string serverId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var (fromMs, toMs) = Prepare(serverId, from, to, BootRange);
        var drafts = BootTimeline.Build(await insights.BootMarkersAsync(serverId, fromMs, toMs, cancellationToken));
        var boots = new List<BootDto>();
        for (var i = 0; i < drafts.Count; i++)
        {
            var boot = drafts[i];
            // Problems while starting up: from the start to "GameReady" (or at most ten minutes if it never got there).
            var startupEnd = boot.ReadyMs ?? Math.Min(boot.StartedMs + 600_000, i + 1 < drafts.Count ? drafts[i + 1].StartedMs : long.MaxValue);
            var (warnings, errors) = await insights.ProblemCountsAsync(serverId, boot.StartedMs, startupEnd, cancellationToken);
            boots.Add(new BootDto(
                DateTimeOffset.FromUnixTimeMilliseconds(boot.StartedMs),
                boot.ReadyMs is { } ready ? DateTimeOffset.FromUnixTimeMilliseconds(ready) : null,
                boot.ReadyMs is { } readyMs ? (readyMs - boot.StartedMs) / 1000.0 : null,
                boot.StoppedMs is { } stopped ? DateTimeOffset.FromUnixTimeMilliseconds(stopped) : null,
                boot.State,
                boot.GameVersion,
                boot.Mods?.Count,
                warnings,
                errors,
                BootTimeline.Diff(i > 0 ? drafts[i - 1].Mods : null, boot.Mods)));
        }

        boots.Reverse();
        return new BootsResponse(boots);
    }

    public async Task<SuggestionsResponse> SuggestAsync(string serverId, string key, string? prefix, string? context, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var (fromMs, toMs) = Prepare(serverId, from, to, DefaultRange);
        var name = key.ToLowerInvariant();
        if (!SqliteLogIndexRepository.CanSuggest(name))
        {
            throw new LogQueryException($"No suggestions for '{key}'.");
        }

        var text = (prefix ?? string.Empty).Trim();
        if (text.Length > 80)
        {
            throw new LogQueryException("The prefix is too long.");
        }

        // Noise is included so every action (clicks too) can be suggested with its real count.
        var filter = new LogFilter(serverId, LogQuery.Parse(context), fromMs, toMs, IncludeNoise: true);
        return new SuggestionsResponse(name, await insights.SuggestAsync(filter, name, text, SuggestionLimit, cancellationToken));
    }

    public async Task<ProblemSummaryResponse> ProblemSummaryAsync(string serverId, CancellationToken cancellationToken)
    {
        if (!_options.Servers.ContainsKey(serverId))
        {
            return new ProblemSummaryResponse(false, null, 0, 0);
        }

        var start = await index.LastServerStartAsync(serverId, cancellationToken);
        if (start is null)
        {
            return new ProblemSummaryResponse(true, null, 0, 0);
        }

        var (errors, warnings) = await insights.NewSignatureCountsAsync(serverId, start.Value, cancellationToken);
        return new ProblemSummaryResponse(true, DateTimeOffset.FromUnixTimeMilliseconds(start.Value), errors, warnings);
    }

    public Task<IReadOnlyList<SavedSearchDto>> SavedSearchesAsync(string userId, string serverId, CancellationToken cancellationToken)
    {
        EnsureConfigured(serverId);
        return insights.SavedSearchesAsync(userId, serverId, cancellationToken);
    }

    public async Task<SavedSearchDto> SaveSearchAsync(string userId, string serverId, SaveSearchRequest request, CancellationToken cancellationToken)
    {
        EnsureConfigured(serverId);
        var name = request.Name?.Trim() ?? string.Empty;
        var query = request.Query?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 80)
        {
            throw new LogQueryException("The name must be 1–80 characters.");
        }

        if (query.Length > 2000)
        {
            throw new LogQueryException("The search is too long to save.");
        }

        if (!Ranges.Contains(request.Range))
        {
            throw new LogQueryException("Unknown time range.");
        }

        LogQuery.Parse(query); // reject searches that would fail when opened
        if (await insights.CountSavedSearchesAsync(userId, serverId, cancellationToken) >= MaximumSavedSearches)
        {
            throw new LogQueryException($"You can save at most {MaximumSavedSearches} searches per server.");
        }

        return await insights.AddSavedSearchAsync(userId, serverId, name, query, request.Range, time.GetUtcNow().ToUnixTimeMilliseconds(), cancellationToken);
    }

    public async Task DeleteSavedSearchAsync(string userId, string serverId, long id, CancellationToken cancellationToken)
    {
        EnsureConfigured(serverId);
        if (!await insights.DeleteSavedSearchAsync(userId, serverId, id, cancellationToken))
        {
            throw new SavedSearchNotFoundException(id);
        }
    }

    private (long From, long To) Prepare(string serverId, DateTimeOffset? from, DateTimeOffset? to, TimeSpan defaultRange)
    {
        EnsureConfigured(serverId);
        return LogTimeRange.Resolve(from, to, time.GetUtcNow(), defaultRange);
    }

    private void EnsureConfigured(string serverId)
    {
        if (!_options.Servers.ContainsKey(serverId))
        {
            throw new ServerLogsNotConfiguredException(serverId);
        }
    }
}
