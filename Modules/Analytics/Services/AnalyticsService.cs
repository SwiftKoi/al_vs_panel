using AlegacyWebPanel.Modules.Analytics.Configuration;
using AlegacyWebPanel.Modules.Analytics.Contracts;
using AlegacyWebPanel.Modules.Analytics.Exceptions;
using AlegacyWebPanel.Modules.Analytics.Persistence;
using AlegacyWebPanel.Modules.ServerManagement.Services;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.Analytics.Services;

public sealed class AnalyticsService(
    IAnalyticsRepository repository,
    IServerManagementService servers,
    IProxyClassifier proxies,
    IOptions<AnalyticsOptions> options,
    TimeProvider time) : IAnalyticsService
{
    public const int MaximumDays = 90;
    public const int MaximumHours = 24 * 30;
    private const int MaximumChartPoints = 720;
    private const int MaximumPlayers = 50;
    private const int MaximumProfileSessions = 200;
    private const int MaximumRecentDrops = 100;

    // A drop is attributed to an autosave when a tick pause started shortly before it.
    private static readonly TimeSpan AutosaveWindow = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AutosaveGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SimultaneousWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SampleLookback = TimeSpan.FromMinutes(3);
    // A drop follows an overload when a tick of 500 ms or more was logged in the minute before it.
    private static readonly TimeSpan OverloadLookback = TimeSpan.FromSeconds(60);

    private static readonly (string Name, TimeSpan Length)[] Windows =
    [
        ("day", TimeSpan.FromDays(1)),
        ("week", TimeSpan.FromDays(7)),
        ("month", TimeSpan.FromDays(30))
    ];

    public async Task<PlayerSummaryResponse> GetPlayerSummaryAsync(
        string serverId,
        int days,
        CancellationToken cancellationToken)
    {
        await ValidateAsync(serverId, days, cancellationToken);
        var zone = ResolveTimeZone(options.Value.TimeZone);
        var now = time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, zone));
        var firstDay = today.AddDays(-(days - 1));
        var dailyFromUtc = TimeZoneInfo.ConvertTimeToUtc(firstDay.ToDateTime(TimeOnly.MinValue), zone);
        var windowFromUtc = now - Windows.Max(window => window.Length);
        var fromUtc = dailyFromUtc < windowFromUtc ? dailyFromUtc : windowFromUtc;

        var joins = await repository.ListJoinsAsync(serverId, fromUtc, now, cancellationToken);
        var firstJoins = await repository.GetFirstJoinsAsync(serverId, cancellationToken);
        var rounds = await repository.ListSampleRoundsAsync(serverId, dailyFromUtc, now, cancellationToken);

        var windows = Windows
            .Select(window =>
            {
                var from = now - window.Length;
                var stats = Summarize(joins.Where(join => join.OccurredAtUtc >= from), firstJoins, from);
                return new PlayerWindowStats(window.Name, stats.Unique, stats.Proxy, stats.New, stats.Joins);
            })
            .ToArray();

        DateOnly LocalDay(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
        var joinsByDay = joins.ToLookup(join => LocalDay(join.OccurredAtUtc));
        var peakByDay = rounds
            .GroupBy(round => LocalDay(round.SampledAtUtc))
            .ToDictionary(group => group.Key, group => group.Max(round => round.Connections));

        var daily = Enumerable.Range(0, days)
            .Select(offset => firstDay.AddDays(offset))
            .Select(day =>
            {
                var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), zone);
                var stats = Summarize(joinsByDay[day], firstJoins, dayStartUtc);
                return new PlayerDailyStats(
                    day.ToString("yyyy-MM-dd"),
                    stats.Unique,
                    stats.Proxy,
                    stats.New,
                    stats.Joins,
                    peakByDay.TryGetValue(day, out var peak) ? peak : null);
            })
            .ToArray();

        DateTime? recordedSince = firstJoins.Count == 0 ? null : firstJoins.Values.Min();
        return new PlayerSummaryResponse(serverId, zone.Id, proxies.IsConfigured, recordedSince, windows, daily);
    }

    public async Task<DisconnectReportResponse> GetDisconnectReportAsync(
        string serverId,
        int days,
        CancellationToken cancellationToken)
    {
        await ValidateAsync(serverId, days, cancellationToken);
        var zone = ResolveTimeZone(options.Value.TimeZone);
        var now = time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, zone));
        var firstDay = today.AddDays(-(days - 1));
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(firstDay.ToDateTime(TimeOnly.MinValue), zone);

        // Load a margin before the range so sessions that started earlier pair correctly.
        var loadFromUtc = fromUtc - TimeSpan.FromDays(1);
        var joins = await repository.ListJoinsAsync(serverId, loadFromUtc, now, cancellationToken);
        var ends = await repository.ListSessionEndsAsync(serverId, loadFromUtc, now, cancellationToken);
        var failures = await repository.ListFailuresAsync(serverId, fromUtc, now, cancellationToken);
        var pauses = await repository.ListPausesAsync(serverId, fromUtc - AutosaveWindow, now, cancellationToken);
        var samples = await repository.ListSamplesAsync(serverId, fromUtc - SampleLookback, now, cancellationToken);
        var overloads = await repository.ListOverloadsAsync(serverId, fromUtc - OverloadLookback, now, cancellationToken);

        var sessions = SessionBuilder.Build(joins, ends)
            .Where(session => session.StartedAtUtc >= fromUtc)
            .ToArray();
        var drops = sessions.Where(session => session.IsDrop).ToArray();
        var pauseStarts = pauses.Select(pause => pause.StartedAtUtc).Order().ToArray();
        var samplesByPlayer = samples
            .Where(sample => sample.PlayerName is not null)
            .ToLookup(sample => sample.PlayerName!, StringComparer.Ordinal);

        var dropEvents = drops
            .Select(drop =>
            {
                var at = drop.EndedAtUtc!.Value;
                var nearAutosave = pauseStarts.Any(start => start >= at - AutosaveWindow && start <= at + AutosaveGrace);
                var slowestTick = overloads
                    .Where(overload => overload.OccurredAtUtc <= at + AutosaveGrace && overload.OccurredAtUtc >= at - OverloadLookback)
                    .Select(overload => (int?)overload.TickMs)
                    .Max();
                var simultaneous = drops.Count(other =>
                    other.PlayerName != drop.PlayerName &&
                    (other.EndedAtUtc!.Value - at).Duration() <= SimultaneousWindow);
                var recent = samplesByPlayer[drop.PlayerName]
                    .Where(sample => sample.SampledAtUtc <= at && sample.SampledAtUtc >= at - SampleLookback)
                    .OrderByDescending(sample => sample.SampledAtUtc)
                    .ToArray();
                return new DropEvent(
                    at,
                    drop.PlayerName,
                    drop.EndKind,
                    drop.EndReason,
                    Minutes(drop),
                    proxies.IsProxy(drop.RemoteAddress),
                    nearAutosave,
                    slowestTick,
                    simultaneous,
                    recent.FirstOrDefault()?.RttMs,
                    recent.FirstOrDefault()?.RetransmitPercent,
                    recent.Length == 0 ? null : recent.Max(sample => sample.LastReceiveMs));
            })
            .OrderByDescending(drop => drop.OccurredAtUtc)
            .ToArray();

        var closed = sessions.Where(session => session.EndedAtUtc is not null).ToArray();
        var proxySessions = sessions.Where(session => proxies.IsProxy(session.RemoteAddress)).ToArray();
        var summary = new DisconnectSummary(
            sessions.Length,
            drops.Length,
            sessions.Count(session => session.QuickRejoin),
            dropEvents.Count(drop => drop.NearAutosave),
            dropEvents.Count(drop => drop.SimultaneousDrops > 0),
            dropEvents.Count(drop => drop.SlowestTickMs is not null),
            Median(closed.Select(Minutes)),
            proxySessions.Length,
            proxySessions.Count(session => session.IsDrop),
            sessions.Length - proxySessions.Length,
            drops.Length - proxySessions.Count(session => session.IsDrop),
            failures.Count);

        var endReasons = sessions
            .GroupBy(session => session.EndKind)
            .Select(group => new SessionEndCount(group.Key, group.Count()))
            .OrderByDescending(count => count.Count)
            .ToArray();
        var failureReasons = failures
            .GroupBy(failure => failure.Reason, StringComparer.Ordinal)
            .Select(group => new FailureReasonCount(group.Key, group.Count()))
            .OrderByDescending(count => count.Count)
            .ToArray();

        DateOnly LocalDay(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
        var sessionsByDay = sessions.ToLookup(session => LocalDay(session.StartedAtUtc));
        var daily = Enumerable.Range(0, days)
            .Select(offset => firstDay.AddDays(offset))
            .Select(day => new DisconnectDailyStats(
                day.ToString("yyyy-MM-dd"),
                sessionsByDay[day].Count(),
                sessionsByDay[day].Count(session => session.IsDrop),
                sessionsByDay[day].Count(session => session.QuickRejoin)))
            .ToArray();

        var players = sessions
            .GroupBy(session => session.PlayerName, StringComparer.Ordinal)
            .Select(group =>
            {
                var playerClosed = group.Where(session => session.EndedAtUtc is not null).ToArray();
                var playerDrops = group.Where(session => session.IsDrop).ToArray();
                return new PlayerDisconnectStats(
                    group.Key,
                    group.Count(),
                    playerDrops.Length,
                    group.Count(session => session.QuickRejoin),
                    playerClosed.Length == 0 ? 0 : Math.Round(playerClosed.Average(Minutes), 1),
                    group.Any(session => proxies.IsProxy(session.RemoteAddress)),
                    playerDrops.Length == 0 ? null : playerDrops.Max(session => session.EndedAtUtc));
            })
            .Where(player => player.Drops > 0 || player.QuickRejoins > 0)
            .OrderByDescending(player => player.Drops + player.QuickRejoins)
            .ThenByDescending(player => player.Drops)
            .ThenBy(player => player.PlayerName, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumPlayers)
            .ToArray();

        return new DisconnectReportResponse(
            serverId,
            zone.Id,
            days,
            summary,
            endReasons,
            failureReasons,
            daily,
            players,
            dropEvents.Take(MaximumRecentDrops).ToArray());
    }

    public async Task<ConnectionQualityResponse> GetConnectionQualityAsync(
        string serverId,
        int hours,
        CancellationToken cancellationToken)
    {
        await ValidateServerAsync(serverId, cancellationToken);
        ValidateHours(hours);
        var now = time.GetUtcNow().UtcDateTime;
        var samples = await repository.ListSamplesAsync(serverId, now.AddHours(-hours), now, cancellationToken);
        var intervals = ConnectionQualityCalculator.WithIntervals(samples.Where(sample => sample.PlayerName is not null));
        var proxy = intervals.Where(interval => proxies.IsProxy(interval.Sample.RemoteAddress)).ToArray();
        var direct = intervals.Where(interval => !proxies.IsProxy(interval.Sample.RemoteAddress)).ToArray();

        var players = intervals
            .GroupBy(interval => interval.Sample.PlayerName!, StringComparer.Ordinal)
            .Select(group => new PlayerConnectionQuality(
                group.Key,
                group.Any(interval => proxies.IsProxy(interval.Sample.RemoteAddress)),
                group.Max(interval => interval.Sample.SampledAtUtc),
                ConnectionQualityCalculator.Summarize(group.ToArray())))
            .OrderByDescending(player => player.Stats.LossPercent ?? 0)
            .ThenByDescending(player => player.Stats.P95RttMs ?? 0)
            .ToArray();

        return new ConnectionQualityResponse(
            serverId,
            hours,
            proxies.IsConfigured,
            ConnectionQualityCalculator.Summarize(intervals),
            ConnectionQualityCalculator.Summarize(proxy),
            ConnectionQualityCalculator.Summarize(direct),
            players);
    }

    public async Task<PlayerConnectionHistoryResponse> GetPlayerConnectionHistoryAsync(
        string serverId,
        string playerName,
        int hours,
        CancellationToken cancellationToken)
    {
        await ValidateServerAsync(serverId, cancellationToken);
        ValidateHours(hours);
        var now = time.GetUtcNow().UtcDateTime;
        var from = now.AddHours(-hours);
        var samples = (await repository.ListSamplesAsync(serverId, from, now, cancellationToken))
            .Where(sample => string.Equals(sample.PlayerName, playerName, StringComparison.Ordinal))
            .ToArray();
        if (samples.Length == 0)
        {
            throw new AnalyticsPlayerNotFoundException(playerName);
        }

        var intervals = ConnectionQualityCalculator.WithIntervals(samples);
        var joins = (await repository.ListJoinsAsync(serverId, from.AddDays(-1), now, cancellationToken))
            .Where(join => join.PlayerName == playerName);
        var ends = (await repository.ListSessionEndsAsync(serverId, from.AddDays(-1), now, cancellationToken))
            .Where(end => end.PlayerName == playerName);
        var sessions = SessionBuilder.Build(joins, ends)
            .Where(session => (session.EndedAtUtc ?? now) >= from)
            .Select(session => new PlayerSessionSpan(session.StartedAtUtc, session.EndedAtUtc, session.EndKind))
            .ToArray();

        return new PlayerConnectionHistoryResponse(
            serverId,
            playerName,
            hours,
            samples.Any(sample => proxies.IsProxy(sample.RemoteAddress)),
            ConnectionQualityCalculator.Summarize(intervals),
            ConnectionQualityCalculator.ToPoints(intervals, MaximumChartPoints),
            sessions);
    }

    public async Task<ActivityHeatmapResponse> GetActivityHeatmapAsync(
        string serverId,
        int days,
        CancellationToken cancellationToken)
    {
        await ValidateAsync(serverId, days, cancellationToken);
        var zone = ResolveTimeZone(options.Value.TimeZone);
        var now = time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, zone));
        var firstDay = today.AddDays(-(days - 1));
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(firstDay.ToDateTime(TimeOnly.MinValue), zone);
        var sessions = await LoadSessionsAsync(serverId, fromUtc, now, cancellationToken);

        // Distinct players online per local (date, hour).
        var online = new Dictionary<(DateOnly Date, int Hour), HashSet<string>>();
        foreach (var session in sessions)
        {
            var start = session.StartedAtUtc < fromUtc ? fromUtc : session.StartedAtUtc;
            var end = SessionBuilder.EffectiveEnd(session, now);
            for (var hour = TruncateToHour(start); hour < end; hour = hour.AddHours(1))
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(hour, zone);
                var key = (DateOnly.FromDateTime(local), local.Hour);
                if (!online.TryGetValue(key, out var players))
                {
                    online[key] = players = new HashSet<string>(StringComparer.Ordinal);
                }

                players.Add(session.PlayerName);
            }
        }

        var dates = Enumerable.Range(0, days).Select(offset => firstDay.AddDays(offset)).ToArray();
        var cells = new List<HeatmapCell>(7 * 24);
        for (var weekday = 0; weekday < 7; weekday++)
        {
            var matching = dates.Where(date => MondayFirst(date.DayOfWeek) == weekday).ToArray();
            for (var hour = 0; hour < 24; hour++)
            {
                var counts = matching
                    .Select(date => online.TryGetValue((date, hour), out var players) ? players.Count : 0)
                    .ToArray();
                cells.Add(new HeatmapCell(
                    weekday,
                    hour,
                    counts.Length == 0 ? 0 : Math.Round((decimal)counts.Average(), 1),
                    counts.Length == 0 ? 0 : counts.Max()));
            }
        }

        return new ActivityHeatmapResponse(serverId, zone.Id, days, cells);
    }

    public async Task<PlayerListResponse> GetPlayerListAsync(
        string serverId,
        int days,
        CancellationToken cancellationToken)
    {
        await ValidateAsync(serverId, days, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var fromUtc = now.AddDays(-days);
        var sessions = await LoadSessionsAsync(serverId, fromUtc, now, cancellationToken);
        var firstJoins = await repository.GetFirstJoinsAsync(serverId, cancellationToken);

        var players = sessions
            .GroupBy(session => session.PlayerName, StringComparer.Ordinal)
            .Select(group =>
            {
                var minutes = group.Select(session => PlayedMinutes(session, now)).ToArray();
                return new PlayerPlaytime(
                    group.Key,
                    group.Count(),
                    Math.Round(minutes.Sum(), 0),
                    Math.Round(minutes.Average(), 1),
                    group.Count(session => session.IsDrop),
                    group.Count(session => session.QuickRejoin),
                    group.Any(session => proxies.IsProxy(session.RemoteAddress)),
                    firstJoins.TryGetValue(group.Key, out var first) ? first : group.Min(session => session.StartedAtUtc),
                    group.Max(session => SessionBuilder.EffectiveEnd(session, now)));
            })
            .OrderByDescending(player => player.TotalMinutes)
            .ToArray();

        return new PlayerListResponse(serverId, days, players);
    }

    public async Task<PlayerProfileResponse> GetPlayerProfileAsync(
        string serverId,
        string playerName,
        int days,
        CancellationToken cancellationToken)
    {
        await ValidateAsync(serverId, days, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var fromUtc = now.AddDays(-days);
        var sessions = (await LoadSessionsAsync(serverId, fromUtc, now, cancellationToken))
            .Where(session => string.Equals(session.PlayerName, playerName, StringComparison.Ordinal))
            .OrderByDescending(session => session.StartedAtUtc)
            .ToArray();
        var firstJoins = await repository.GetFirstJoinsAsync(serverId, cancellationToken);
        if (sessions.Length == 0 || !firstJoins.TryGetValue(playerName, out var firstSeen))
        {
            throw new AnalyticsPlayerNotFoundException(playerName);
        }

        var samples = (await repository.ListSamplesAsync(serverId, fromUtc, now, cancellationToken))
            .Where(sample => string.Equals(sample.PlayerName, playerName, StringComparison.Ordinal));
        var minutes = sessions.Select(session => PlayedMinutes(session, now)).ToArray();

        return new PlayerProfileResponse(
            serverId,
            playerName,
            days,
            firstSeen,
            sessions.Max(session => SessionBuilder.EffectiveEnd(session, now)),
            Math.Round(sessions.Count(session => proxies.IsProxy(session.RemoteAddress)) * 100m / sessions.Length, 0),
            sessions.Length,
            Math.Round(minutes.Sum(), 0),
            Math.Round(minutes.Average(), 1),
            sessions.Count(session => session.IsDrop),
            sessions.Count(session => session.QuickRejoin),
            sessions
                .GroupBy(session => session.EndKind)
                .Select(group => new SessionEndCount(group.Key, group.Count()))
                .OrderByDescending(count => count.Count)
                .ToArray(),
            ConnectionQualityCalculator.Summarize(ConnectionQualityCalculator.WithIntervals(samples)),
            sessions
                .Take(MaximumProfileSessions)
                .Select(session => new PlayerSessionRow(
                    session.StartedAtUtc,
                    session.EndedAtUtc,
                    Math.Round(PlayedMinutes(session, now), 1),
                    session.EndKind,
                    session.EndReason,
                    proxies.IsProxy(session.RemoteAddress),
                    session.QuickRejoin))
                .ToArray());
    }

    private async Task<IReadOnlyList<PlayerSession>> LoadSessionsAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        // Load a margin before the range so sessions that started earlier pair correctly.
        var loadFrom = fromUtc - TimeSpan.FromDays(1);
        var joins = await repository.ListJoinsAsync(serverId, loadFrom, toUtc, cancellationToken);
        var ends = await repository.ListSessionEndsAsync(serverId, loadFrom, toUtc, cancellationToken);
        return SessionBuilder.Build(joins, ends)
            .Where(session => SessionBuilder.EffectiveEnd(session, toUtc) >= fromUtc)
            .ToArray();
    }

    private static decimal PlayedMinutes(PlayerSession session, DateTime nowUtc) =>
        (decimal)Math.Max(0, (SessionBuilder.EffectiveEnd(session, nowUtc) - session.StartedAtUtc).TotalMinutes);

    private static DateTime TruncateToHour(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);

    private static int MondayFirst(DayOfWeek day) => ((int)day + 6) % 7;

    public async Task<ServerHealthResponse> GetServerHealthAsync(
        string serverId,
        int hours,
        CancellationToken cancellationToken)
    {
        await ValidateServerAsync(serverId, cancellationToken);
        ValidateHours(hours);
        var now = time.GetUtcNow().UtcDateTime;
        var from = now.AddHours(-hours);
        var metrics = await repository.ListMetricSamplesAsync(serverId, from, now, cancellationToken);
        var connections = await repository.ListSamplesAsync(serverId, from, now, cancellationToken);
        var pauses = await repository.ListPausesAsync(serverId, from, now, cancellationToken);
        var overloads = await repository.ListOverloadsAsync(serverId, from, now, cancellationToken);

        var points = ServerHealthCalculator.Build(metrics, connections, pauses, overloads, MaximumChartPoints);
        var longest = pauses
            .OrderByDescending(ServerHealthCalculator.Seconds)
            .ThenByDescending(pause => pause.StartedAtUtc)
            .Take(10)
            .Select(pause => new ServerPauseSpan(pause.StartedAtUtc, ServerHealthCalculator.Seconds(pause)))
            .ToArray();

        return new ServerHealthResponse(
            serverId,
            hours,
            ServerHealthCalculator.Summarize(metrics, points, pauses, overloads),
            points,
            longest,
            overloads
                .OrderByDescending(overload => overload.TickMs)
                .ThenByDescending(overload => overload.OccurredAtUtc)
                .Take(10)
                .Select(overload => new ServerOverloadSpan(overload.OccurredAtUtc, overload.TickMs))
                .ToArray());
    }

    private static void ValidateHours(int hours)
    {
        if (hours is < 1 or > MaximumHours)
        {
            throw new InvalidAnalyticsRangeException($"Hours must be between 1 and {MaximumHours}.");
        }
    }

    private async Task ValidateServerAsync(string serverId, CancellationToken cancellationToken)
    {
        var known = await servers.ListAsync(cancellationToken);
        if (known.All(server => server.Id != serverId))
        {
            throw new AnalyticsServerNotFoundException(serverId);
        }
    }

    private async Task ValidateAsync(string serverId, int days, CancellationToken cancellationToken)
    {
        if (days is < 1 or > MaximumDays)
        {
            throw new InvalidAnalyticsRangeException($"Days must be between 1 and {MaximumDays}.");
        }

        await ValidateServerAsync(serverId, cancellationToken);
    }

    private static decimal Minutes(PlayerSession session) =>
        session.EndedAtUtc is { } ended
            ? Math.Round((decimal)(ended - session.StartedAtUtc).TotalMinutes, 1)
            : 0;

    private static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : Math.Round((sorted[middle - 1] + sorted[middle]) / 2, 1);
    }

    private (int Unique, int Proxy, int New, int Joins) Summarize(
        IEnumerable<PlayerJoin> joins,
        IReadOnlyDictionary<string, DateTime> firstJoins,
        DateTime fromUtc)
    {
        var list = joins.ToList();
        var players = list.Select(join => join.PlayerName).ToHashSet(StringComparer.Ordinal);
        var proxyPlayers = list
            .Where(join => proxies.IsProxy(join.RemoteAddress))
            .Select(join => join.PlayerName)
            .ToHashSet(StringComparer.Ordinal);
        var newPlayers = players.Count(name => firstJoins.TryGetValue(name, out var first) && first >= fromUtc);
        return (players.Count, proxyPlayers.Count, newPlayers, list.Count);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
