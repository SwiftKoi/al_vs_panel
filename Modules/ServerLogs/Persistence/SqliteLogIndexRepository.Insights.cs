using System.Text;
using AlegacyWebPanel.Modules.ServerLogs.Contracts;
using AlegacyWebPanel.Modules.ServerLogs.Services.Parsing;
using Microsoft.Data.Sqlite;

namespace AlegacyWebPanel.Modules.ServerLogs.Persistence;

public sealed partial class SqliteLogIndexRepository
{
    private const long DayMs = 86_400_000;

    public async Task<IReadOnlyList<PlayerSummaryDto>> PlayersAsync(string serverId, long fromMs, long toMs, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT e.actor,
                   SUM(e.action NOT IN ({NoiseList})),
                   SUM(e.action = 'command'), SUM(e.action = 'kill'), SUM(e.action = 'death'),
                   SUM(e.action = 'position-rejected'), SUM(e.action = 'join'), MAX(e.ts)
            FROM entries e
            WHERE e.server_id = $s AND e.kind = 'audit' AND e.actor IS NOT NULL AND e.ts >= $from AND e.ts < $to
            GROUP BY e.actor COLLATE NOCASE
            ORDER BY MAX(e.ts) DESC
            LIMIT $limit
            """;
        AddRange(command, serverId, fromMs, toMs);
        command.Parameters.AddWithValue("$limit", limit);
        var players = new List<PlayerSummaryDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            players.Add(new PlayerSummaryDto(
                reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4),
                reader.GetInt64(5), reader.GetInt64(6), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7))));
        }

        return players;
    }

    public async Task<(long? FirstMs, long? LastMs)> PlayerSeenAsync(string serverId, string player, long fromMs, long toMs, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MIN(e.ts), MAX(e.ts) FROM entries e
            WHERE e.server_id = $s AND e.actor = $p COLLATE NOCASE AND e.ts >= $from AND e.ts < $to
            """;
        AddRange(command, serverId, fromMs, toMs);
        command.Parameters.AddWithValue("$p", player);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.IsDBNull(0) ? null : reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1));
    }

    public async Task<IReadOnlyList<FacetValue>> ActionCountsAsync(string serverId, string? player, LogPlaceFilter? place, long fromMs, long toMs, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var where = AuditWhere(command, serverId, player, place, fromMs, toMs);
        command.CommandText = $"SELECT e.action, COUNT(*) FROM entries e WHERE {where} AND e.action IS NOT NULL GROUP BY e.action ORDER BY 2 DESC";
        return await ReadFacetsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<ItemTotalDto>> ItemTotalsAsync(string serverId, string? player, LogPlaceFilter? place, string action, long fromMs, long toMs, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var where = AuditWhere(command, serverId, player, place, fromMs, toMs);
        command.CommandText = $"""
            SELECT e.item, SUM(COALESCE(e.qty, 1)), COUNT(*) FROM entries e
            WHERE {where} AND e.action = $action AND e.item IS NOT NULL
            GROUP BY e.item ORDER BY 2 DESC LIMIT $limit
            """;
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$limit", limit);
        var items = new List<ItemTotalDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ItemTotalDto(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        }

        return items;
    }

    public async Task<IReadOnlyList<LogEntryDto>> PlayerEntriesAsync(string serverId, string player, IReadOnlyList<string> actions, long fromMs, long toMs, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var where = new StringBuilder(AuditWhere(command, serverId, player, null, fromMs, toMs));
        AppendIn(where, command, "e.action", "pa", actions.ToArray(), negate: false);
        command.CommandText = $"SELECT {EntryColumns} FROM entries e WHERE {where} ORDER BY e.ts DESC, e.id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        return await ReadEntriesAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<PlaceDto>> PlacesAsync(string serverId, string player, long fromMs, long toMs, int gridSize, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var where = AuditWhere(command, serverId, player, null, fromMs, toMs);
        // Cells of gridSize blocks (floor division, also for negative coordinates); the reported point is the busiest cell's average position.
        command.CommandText = $"""
            SELECT CAST(AVG(e.x) AS INTEGER), CAST(AVG(e.y) AS INTEGER), CAST(AVG(e.z) AS INTEGER), COUNT(*), MAX(e.ts)
            FROM entries e
            WHERE {where} AND e.x IS NOT NULL AND e.z IS NOT NULL AND e.action NOT IN ({NoiseList}, 'position-rejected', 'teleport')
            GROUP BY (e.x - (e.x < 0) * ($grid - 1)) / $grid, (e.z - (e.z < 0) * ($grid - 1)) / $grid
            ORDER BY 4 DESC LIMIT $limit
            """;
        command.Parameters.AddWithValue("$grid", gridSize);
        command.Parameters.AddWithValue("$limit", limit);
        var places = new List<PlaceDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            places.Add(new PlaceDto(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.GetInt32(2),
                reader.GetInt64(3), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4))));
        }

        return places;
    }

    public async Task<IReadOnlyList<DayActivityDto>> DailyActivityAsync(string serverId, string player, long fromMs, long toMs, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var where = AuditWhere(command, serverId, player, null, fromMs, toMs);
        command.CommandText = $"""
            SELECT e.ts / {DayMs}, SUM(e.action NOT IN ({NoiseList}, 'position-rejected')), SUM(e.action = 'position-rejected')
            FROM entries e WHERE {where} GROUP BY 1 ORDER BY 1
            """;
        var days = new List<DayActivityDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            days.Add(new DayActivityDto(DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0) * DayMs), reader.GetInt64(1), reader.GetInt64(2)));
        }

        return days;
    }

    public async Task<IReadOnlyList<(string Player, string Action, long Count, long FirstMs, long LastMs)>> LocationActivityAsync(
        string serverId, LogPlaceFilter place, long fromMs, long toMs, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var where = AuditWhere(command, serverId, null, place, fromMs, toMs);
        command.CommandText = $"""
            SELECT e.actor, e.action, COUNT(*), MIN(e.ts), MAX(e.ts) FROM entries e
            WHERE {where} AND e.actor IS NOT NULL AND e.action NOT IN ({NoiseList})
            GROUP BY e.actor COLLATE NOCASE, e.action
            """;
        var rows = new List<(string, string, long, long, long)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4)));
        }

        return rows;
    }

    // What each autocomplete key suggests: the value expression and, for shortcut keys, the action it belongs to.
    // Keys and SQL come from this table only, never from the request.
    private static readonly Dictionary<string, (string Expression, string? Action)> SuggestKeys = new(StringComparer.Ordinal)
    {
        ["player"] = ("e.actor", null),
        ["item"] = ("e.item", null),
        ["source"] = ("e.source", null),
        ["level"] = ("e.level", null),
        ["action"] = ("e.action", null),
        ["log"] = ("e.kind", null),
        ["with"] = ("e.other", null),
        ["command"] = ("e.item", "command"),
        ["killed"] = ("COALESCE(e.other, e.item)", "kill"),
        ["killedby"] = ("e.other", "death"),
        ["took"] = ("e.item", "take"),
        ["put"] = ("e.item", "put"),
        ["placed"] = ("e.item", "place"),
        ["broke"] = ("e.item", "break"),
        ["gave"] = ("e.item", "give")
    };

    public static bool CanSuggest(string key) => SuggestKeys.ContainsKey(key);

    public async Task<IReadOnlyList<FacetValue>> SuggestAsync(LogFilter context, string key, string prefix, int limit, CancellationToken cancellationToken)
    {
        var (expression, action) = SuggestKeys[key];
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // The rest of the search box narrows the values: "player:Orex_ killed:" lists what Orex_ killed.
        var where = BuildWhere(context, command);
        if (action is not null)
        {
            where.Append(" AND e.action = $sact");
            command.Parameters.AddWithValue("$sact", action);
        }

        var text = key == "command" ? "/" + prefix.TrimStart('/') : prefix;
        where.Append($" AND {expression} IS NOT NULL AND ({expression} LIKE $prefix ESCAPE '\\'");
        // Codes like "game:ingot-iron" also match after their "domain:", so "ingot" finds them.
        where.Append($" OR {expression} LIKE $inner ESCAPE '\\')");
        command.Parameters.AddWithValue("$prefix", EscapeLike(text) + "%");
        command.Parameters.AddWithValue("$inner", "%:" + EscapeLike(text) + "%");
        command.CommandText = $"""
            SELECT {expression}, COUNT(*) FROM entries e WHERE {where}
            GROUP BY {expression} COLLATE NOCASE ORDER BY 2 DESC LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", limit);
        return await ReadFacetsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<BootMarker>> BootMarkersAsync(string serverId, long fromMs, long toMs, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // FTS narrows ~all main lines down to the handful of startup/shutdown markers; the exact match follows.
        command.CommandText = """
            SELECT e.ts, e.message, e.extra FROM entries e
            WHERE e.server_id = $s AND e.kind = 'main' AND e.ts >= $from AND e.ts < $to
              AND e.id IN (SELECT rowid FROM entries_fts WHERE entries_fts MATCH
                  '"Server logger started" OR "Game Version" OR "Loaded mods" OR "runphase GameReady" OR "Stopped the server"')
              AND (e.message = 'Server logger started.' OR e.message LIKE 'Game Version:%' OR e.message LIKE 'Loaded mods (%'
                   OR e.message = 'Entering runphase GameReady' OR e.message = 'Stopped the server!')
            ORDER BY e.ts, e.id
            """;
        AddRange(command, serverId, fromMs, toMs);
        var markers = new List<BootMarker>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            markers.Add(new BootMarker(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return markers;
    }

    public async Task<(long Warnings, long Errors)> ProblemCountsAsync(string serverId, long fromMs, long toMs, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(e.level = 'Warning'), 0), COALESCE(SUM(e.level IN ('Error', 'Fatal')), 0) FROM entries e
            WHERE e.server_id = $s AND e.kind = 'main' AND e.ts >= $from AND e.ts <= $to AND e.signature_id IS NOT NULL
            """;
        AddRange(command, serverId, fromMs, toMs);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    public async Task<(long Errors, long Warnings)> NewSignatureCountsAsync(string serverId, long sinceMs, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(level IN ('Error', 'Fatal')), 0), COALESCE(SUM(level = 'Warning'), 0) FROM signatures
            WHERE server_id = $s AND first_ts >= $since AND muted = 0
            """;
        command.Parameters.AddWithValue("$s", serverId);
        command.Parameters.AddWithValue("$since", sinceMs);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    public async Task<IReadOnlyList<SavedSearchDto>> SavedSearchesAsync(string userId, string serverId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, query, range, created_at FROM saved_searches WHERE user_id = $u AND server_id = $s ORDER BY name COLLATE NOCASE";
        command.Parameters.AddWithValue("$u", userId);
        command.Parameters.AddWithValue("$s", serverId);
        var searches = new List<SavedSearchDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            searches.Add(new SavedSearchDto(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4))));
        }

        return searches;
    }

    public async Task<SavedSearchDto> AddSavedSearchAsync(string userId, string serverId, string name, string query, string range, long createdMs, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO saved_searches (user_id, server_id, name, query, range, created_at) VALUES ($u, $s, $n, $q, $r, $c) RETURNING id
            """;
        command.Parameters.AddWithValue("$u", userId);
        command.Parameters.AddWithValue("$s", serverId);
        command.Parameters.AddWithValue("$n", name);
        command.Parameters.AddWithValue("$q", query);
        command.Parameters.AddWithValue("$r", range);
        command.Parameters.AddWithValue("$c", createdMs);
        var id = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        return new SavedSearchDto(id, name, query, range, DateTimeOffset.FromUnixTimeMilliseconds(createdMs));
    }

    public async Task<bool> DeleteSavedSearchAsync(string userId, string serverId, long id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM saved_searches WHERE id = $id AND user_id = $u AND server_id = $s";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$u", userId);
        command.Parameters.AddWithValue("$s", serverId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<int> CountSavedSearchesAsync(string userId, string serverId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM saved_searches WHERE user_id = $u AND server_id = $s";
        command.Parameters.AddWithValue("$u", userId);
        command.Parameters.AddWithValue("$s", serverId);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static string NoiseList => string.Join(", ", AuditParser.NoiseActions.Select(action => $"'{action}'"));

    private static void AddRange(SqliteCommand command, string serverId, long fromMs, long toMs)
    {
        command.Parameters.AddWithValue("$s", serverId);
        command.Parameters.AddWithValue("$from", fromMs);
        command.Parameters.AddWithValue("$to", toMs);
    }

    private static string AuditWhere(SqliteCommand command, string serverId, string? player, LogPlaceFilter? place, long fromMs, long toMs)
    {
        AddRange(command, serverId, fromMs, toMs);
        var where = new StringBuilder("e.server_id = $s AND e.kind = 'audit' AND e.ts >= $from AND e.ts < $to");
        if (player is not null)
        {
            where.Append(" AND e.actor = $p COLLATE NOCASE");
            command.Parameters.AddWithValue("$p", player);
        }

        if (place is not null)
        {
            where.Append(" AND e.x BETWEEN $px0 AND $px1 AND e.z BETWEEN $pz0 AND $pz1");
            command.Parameters.AddWithValue("$px0", place.X - place.Radius);
            command.Parameters.AddWithValue("$px1", place.X + place.Radius);
            command.Parameters.AddWithValue("$pz0", place.Z - place.Radius);
            command.Parameters.AddWithValue("$pz1", place.Z + place.Radius);
            if (place.Y is { } y)
            {
                where.Append(" AND e.y BETWEEN $py0 AND $py1");
                command.Parameters.AddWithValue("$py0", y - place.Radius);
                command.Parameters.AddWithValue("$py1", y + place.Radius);
            }
        }

        return where.ToString();
    }

    private static async Task<IReadOnlyList<FacetValue>> ReadFacetsAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var values = new List<FacetValue>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new FacetValue(reader.GetString(0), reader.GetInt64(1)));
        }

        return values;
    }
}
