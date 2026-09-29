using System.Text;
using AlegacyWebPanel.Modules.ServerLogs.Contracts;
using AlegacyWebPanel.Modules.ServerLogs.Services.Parsing;
using Microsoft.Data.Sqlite;

namespace AlegacyWebPanel.Modules.ServerLogs.Persistence;

/// <summary>
/// Log entries in SQLite. <c>entries_fts</c> is an external-content FTS5 index kept in sync by triggers;
/// every query goes through parameters, and user text reaches FTS only as quoted terms (<see cref="Services.LogQuery"/>).
/// </summary>
public sealed partial class SqliteLogIndexRepository(string databasePath) : ILogIndexRepository, ILogInsightsRepository
{
    private const string EntryColumns =
        "e.id, e.ts, e.kind, e.level, e.source, e.actor, e.action, e.item, e.x, e.y, e.z, e.message, e.extra, e.signature_id, e.other";

    private static readonly string[] Schema =
    [
        "PRAGMA journal_mode = WAL",
        """
        CREATE TABLE IF NOT EXISTS log_files (
            id INTEGER PRIMARY KEY,
            server_id TEXT NOT NULL,
            identity TEXT NOT NULL,
            kind TEXT NOT NULL,
            path TEXT NOT NULL,
            size INTEGER NOT NULL DEFAULT 0,
            offset INTEGER NOT NULL DEFAULT 0,
            last_entry_id INTEGER NULL,
            UNIQUE (server_id, kind, identity))
        """,
        """
        CREATE TABLE IF NOT EXISTS entries (
            id INTEGER PRIMARY KEY,
            server_id TEXT NOT NULL,
            file_id INTEGER NOT NULL,
            ts INTEGER NOT NULL,
            kind TEXT NOT NULL,
            level TEXT NOT NULL,
            source TEXT NULL,
            actor TEXT NULL,
            action TEXT NULL,
            item TEXT NULL,
            x INTEGER NULL,
            y INTEGER NULL,
            z INTEGER NULL,
            signature_id INTEGER NULL,
            message TEXT NOT NULL,
            extra TEXT NULL)
        """,
        "CREATE INDEX IF NOT EXISTS ix_entries_time ON entries (server_id, ts, id)",
        "CREATE INDEX IF NOT EXISTS ix_entries_kind_time ON entries (server_id, kind, ts)",
        "CREATE INDEX IF NOT EXISTS ix_entries_actor ON entries (server_id, actor COLLATE NOCASE, ts) WHERE actor IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS ix_entries_signature ON entries (server_id, signature_id, ts) WHERE signature_id IS NOT NULL",
        """
        CREATE VIRTUAL TABLE IF NOT EXISTS entries_fts USING fts5(
            message, extra, content = 'entries', content_rowid = 'id', tokenize = 'unicode61 remove_diacritics 2')
        """,
        """
        CREATE TRIGGER IF NOT EXISTS entries_ai AFTER INSERT ON entries BEGIN
            INSERT INTO entries_fts (rowid, message, extra) VALUES (new.id, new.message, new.extra);
        END
        """,
        """
        CREATE TRIGGER IF NOT EXISTS entries_ad AFTER DELETE ON entries BEGIN
            INSERT INTO entries_fts (entries_fts, rowid, message, extra) VALUES ('delete', old.id, old.message, old.extra);
        END
        """,
        """
        CREATE TRIGGER IF NOT EXISTS entries_au AFTER UPDATE OF message, extra ON entries BEGIN
            INSERT INTO entries_fts (entries_fts, rowid, message, extra) VALUES ('delete', old.id, old.message, old.extra);
            INSERT INTO entries_fts (rowid, message, extra) VALUES (new.id, new.message, new.extra);
        END
        """,
        """
        CREATE TABLE IF NOT EXISTS signatures (
            id INTEGER PRIMARY KEY,
            server_id TEXT NOT NULL,
            kind TEXT NOT NULL,
            level TEXT NOT NULL,
            source TEXT NULL,
            template TEXT NOT NULL,
            first_ts INTEGER NOT NULL,
            muted INTEGER NOT NULL DEFAULT 0,
            UNIQUE (server_id, kind, level, template))
        """,
        """
        CREATE TABLE IF NOT EXISTS saved_searches (
            id INTEGER PRIMARY KEY,
            user_id TEXT NOT NULL,
            server_id TEXT NOT NULL,
            name TEXT NOT NULL,
            query TEXT NOT NULL,
            range TEXT NOT NULL,
            created_at INTEGER NOT NULL)
        """,
        "CREATE INDEX IF NOT EXISTS ix_saved_searches_user ON saved_searches (user_id, server_id)"
    ];

    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Private,
        DefaultTimeout = 30
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        foreach (var statement in Schema)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await MigrateAsync(connection, cancellationToken);
    }

    private const int SchemaVersion = 2;

    /// <summary>
    /// Columns added after the first release (qty, other) and, when the audit parser learned more, a re-parse of
    /// the stored audit lines so old entries get the new fields. Tracked with SQLite's user_version.
    /// </summary>
    private static async Task MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        async Task<long> ScalarAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        async Task ExecuteAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (await ScalarAsync("PRAGMA user_version") >= SchemaVersion)
        {
            return;
        }

        foreach (var column in new[] { "qty", "other" })
        {
            if (await ScalarAsync($"SELECT COUNT(*) FROM pragma_table_info('entries') WHERE name = '{column}'") == 0)
            {
                await ExecuteAsync($"ALTER TABLE entries ADD COLUMN {column} {(column == "qty" ? "INTEGER" : "TEXT")} NULL");
            }
        }

        await ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_entries_action ON entries (server_id, action, ts) WHERE action IS NOT NULL");

        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken))
        {
            var updates = new List<(long Id, AuditInfo Info)>();
            await using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT id, message FROM entries WHERE kind = 'audit'";
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    updates.Add((reader.GetInt64(0), AuditParser.Parse(reader.GetString(1))));
                }
            }

            await using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE entries SET actor = $a, action = $act, item = $item, qty = $qty, other = $o, x = $x, y = $y, z = $z WHERE id = $id
                """;
            var parameters = new[] { "$a", "$act", "$item", "$qty", "$o", "$x", "$y", "$z", "$id" }
                .ToDictionary(name => name, name => update.Parameters.AddWithValue(name, DBNull.Value));
            foreach (var (id, info) in updates)
            {
                parameters["$a"].Value = (object?)info.Player ?? DBNull.Value;
                parameters["$act"].Value = info.Action;
                parameters["$item"].Value = (object?)info.Item ?? DBNull.Value;
                parameters["$qty"].Value = (object?)info.Quantity ?? DBNull.Value;
                parameters["$o"].Value = (object?)info.Other ?? DBNull.Value;
                parameters["$x"].Value = (object?)info.X ?? DBNull.Value;
                parameters["$y"].Value = (object?)info.Y ?? DBNull.Value;
                parameters["$z"].Value = (object?)info.Z ?? DBNull.Value;
                parameters["$id"].Value = id;
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            // Player kills: the victim is the one the killer damaged last, within a few seconds before.
            await using (var victims = connection.CreateCommand())
            {
                victims.Transaction = transaction;
                victims.CommandText = """
                    UPDATE entries SET other = (
                        SELECT d.actor FROM entries d
                        WHERE d.server_id = entries.server_id AND d.action = 'damage' AND d.other = entries.actor COLLATE NOCASE
                          AND d.ts BETWEEN entries.ts - 5000 AND entries.ts
                        ORDER BY d.ts DESC, d.id DESC LIMIT 1)
                    WHERE action = 'kill' AND item = 'game:player' AND other IS NULL
                    """;
                await victims.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var version = connection.CreateCommand())
            {
                version.Transaction = transaction;
                version.CommandText = $"PRAGMA user_version = {SchemaVersion}";
                await version.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<TrackedLogFile>> GetFilesAsync(string serverId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, identity, kind, path, size, offset, last_entry_id FROM log_files WHERE server_id = $s";
        command.Parameters.AddWithValue("$s", serverId);
        var files = new List<TrackedLogFile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(new TrackedLogFile(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt64(4), reader.GetInt64(5), reader.IsDBNull(6) ? null : reader.GetInt64(6)));
        }

        return files;
    }

    public async Task<TrackedLogFile> AddFileAsync(string serverId, string identity, string kind, string path, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO log_files (server_id, identity, kind, path) VALUES ($s, $i, $k, $p)
            ON CONFLICT (server_id, kind, identity) DO UPDATE SET path = excluded.path
            RETURNING id, size, offset, last_entry_id
            """;
        command.Parameters.AddWithValue("$s", serverId);
        command.Parameters.AddWithValue("$i", identity);
        command.Parameters.AddWithValue("$k", kind);
        command.Parameters.AddWithValue("$p", path);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new TrackedLogFile(reader.GetInt64(0), identity, kind, path, reader.GetInt64(1), reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3));
    }

    public async Task UpdateFileAsync(long fileId, string path, long size, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE log_files SET path = $p, size = $z WHERE id = $id";
        command.Parameters.AddWithValue("$p", path);
        command.Parameters.AddWithValue("$z", size);
        command.Parameters.AddWithValue("$id", fileId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long?> AppendAsync(string serverId, TrackedLogFile file, long newOffset, long size, LogChunk chunk,
        long minimumTimestampMs, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var lastEntryId = file.LastEntryId;

        if (chunk.LeadingContinuation is { } continuation && lastEntryId is { } previous)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE entries SET extra = substr(CASE WHEN extra IS NULL THEN $c ELSE extra || char(10) || $c END, 1, $max)
                WHERE id = $id
                """;
            update.Parameters.AddWithValue("$c", continuation);
            update.Parameters.AddWithValue("$max", LogLineParser.MaximumExtraLength);
            update.Parameters.AddWithValue("$id", previous);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO entries (server_id, file_id, ts, kind, level, source, actor, action, item, qty, other, x, y, z, signature_id, message, extra)
            VALUES ($s, $f, $ts, $k, $l, $src, $a, $act, $item, $qty, $other, $x, $y, $z, $sig, $m, $e)
            RETURNING id
            """;
        var parameters = new[] { "$s", "$f", "$ts", "$k", "$l", "$src", "$a", "$act", "$item", "$qty", "$other", "$x", "$y", "$z", "$sig", "$m", "$e" }
            .ToDictionary(name => name, name => insert.Parameters.AddWithValue(name, DBNull.Value));
        parameters["$s"].Value = serverId;
        parameters["$f"].Value = file.Id;

        await using var signature = connection.CreateCommand();
        signature.CommandText = """
            INSERT INTO signatures (server_id, kind, level, source, template, first_ts) VALUES ($s, $k, $l, $src, $t, $ts)
            ON CONFLICT (server_id, kind, level, template) DO UPDATE SET first_ts = min(first_ts, excluded.first_ts)
            RETURNING id
            """;
        var sigServer = signature.Parameters.AddWithValue("$s", DBNull.Value);
        var sigKind = signature.Parameters.AddWithValue("$k", DBNull.Value);
        var sigLevel = signature.Parameters.AddWithValue("$l", DBNull.Value);
        var sigSource = signature.Parameters.AddWithValue("$src", DBNull.Value);
        var sigTemplate = signature.Parameters.AddWithValue("$t", DBNull.Value);
        var sigTimestamp = signature.Parameters.AddWithValue("$ts", DBNull.Value);
        sigServer.Value = serverId;

        foreach (var entry in chunk.Entries)
        {
            if (entry.TimestampMs < minimumTimestampMs)
            {
                // Past retention: skip it, and don't let the next chunk's continuation attach to an older entry.
                lastEntryId = null;
                continue;
            }

            object signatureId = DBNull.Value;
            if (entry.Signature is { } template)
            {
                sigKind.Value = entry.Kind;
                sigLevel.Value = entry.Level;
                sigSource.Value = (object?)entry.Source ?? DBNull.Value;
                sigTemplate.Value = template;
                sigTimestamp.Value = entry.TimestampMs;
                signatureId = (long)(await signature.ExecuteScalarAsync(cancellationToken))!;
            }

            parameters["$ts"].Value = entry.TimestampMs;
            parameters["$k"].Value = entry.Kind;
            parameters["$l"].Value = entry.Level;
            parameters["$src"].Value = (object?)entry.Source ?? DBNull.Value;
            parameters["$a"].Value = (object?)entry.Audit?.Player ?? DBNull.Value;
            parameters["$act"].Value = (object?)entry.Audit?.Action ?? DBNull.Value;
            parameters["$item"].Value = (object?)entry.Audit?.Item ?? DBNull.Value;
            parameters["$qty"].Value = (object?)entry.Audit?.Quantity ?? DBNull.Value;
            parameters["$other"].Value = (object?)entry.Audit?.Other ?? DBNull.Value;
            parameters["$x"].Value = (object?)entry.Audit?.X ?? DBNull.Value;
            parameters["$y"].Value = (object?)entry.Audit?.Y ?? DBNull.Value;
            parameters["$z"].Value = (object?)entry.Audit?.Z ?? DBNull.Value;
            parameters["$sig"].Value = signatureId;
            parameters["$m"].Value = entry.Message;
            parameters["$e"].Value = (object?)entry.Extra ?? DBNull.Value;
            lastEntryId = (long)(await insert.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var move = connection.CreateCommand();
        move.CommandText = "UPDATE log_files SET offset = $o, size = $z, path = $p, last_entry_id = $l WHERE id = $id";
        move.Parameters.AddWithValue("$o", newOffset);
        move.Parameters.AddWithValue("$z", size);
        move.Parameters.AddWithValue("$p", file.Path);
        move.Parameters.AddWithValue("$l", (object?)lastEntryId ?? DBNull.Value);
        move.Parameters.AddWithValue("$id", file.Id);
        await move.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return lastEntryId;
    }

    public async Task<int> PruneAsync(string serverId, string kind, long beforeMs, CancellationToken cancellationToken)
    {
        var total = 0;
        await using var connection = await OpenAsync(cancellationToken);
        while (true)
        {
            // Small batches keep each write lock short while searches run.
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM entries WHERE id IN (
                    SELECT id FROM entries WHERE server_id = $s AND kind = $k AND ts < $t LIMIT 5000)
                """;
            command.Parameters.AddWithValue("$s", serverId);
            command.Parameters.AddWithValue("$k", kind);
            command.Parameters.AddWithValue("$t", beforeMs);
            var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
            total += deleted;
            if (deleted < 5000)
            {
                return total;
            }
        }
    }

    public async Task<IReadOnlyList<LogEntryDto>> SearchAsync(LogFilter filter, LogCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var where = BuildWhere(filter, command);
        if (cursor is not null)
        {
            where.Append(" AND (e.ts < $cts OR (e.ts = $cts AND e.id < $cid))");
            command.Parameters.AddWithValue("$cts", cursor.TimestampMs);
            command.Parameters.AddWithValue("$cid", cursor.Id);
        }

        command.CommandText = $"SELECT {EntryColumns} FROM entries e WHERE {where} ORDER BY e.ts DESC, e.id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        return await ReadEntriesAsync(command, cancellationToken);
    }

    public async Task<LogFacetsResponse> FacetsAsync(LogFilter filter, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);

        async Task<IReadOnlyList<FacetValue>> Facet(string column)
        {
            await using var command = connection.CreateCommand();
            var where = BuildWhere(filter, command);
            command.CommandText = $"""
                SELECT {column}, COUNT(*) FROM entries e WHERE {where} AND {column} IS NOT NULL
                GROUP BY {column} ORDER BY 2 DESC LIMIT $limit
                """;
            command.Parameters.AddWithValue("$limit", limit);
            var values = new List<FacetValue>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                values.Add(new FacetValue(reader.GetString(0), reader.GetInt64(1)));
            }

            return values;
        }

        var logs = await Facet("e.kind");
        return new LogFacetsResponse(
            logs.Sum(value => value.Count),
            logs,
            await Facet("e.level"),
            await Facet("e.source"),
            await Facet("e.actor"),
            await Facet("e.action"));
    }

    public async Task<IReadOnlyList<long>> HistogramAsync(LogFilter filter, long bucketMs, int buckets, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var where = BuildWhere(filter, command);
        command.CommandText = $"SELECT (e.ts - $from) / $bucket, COUNT(*) FROM entries e WHERE {where} GROUP BY 1";
        command.Parameters.AddWithValue("$bucket", bucketMs);
        var counts = new long[buckets];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var bucket = reader.GetInt64(0);
            if (bucket >= 0 && bucket < buckets)
            {
                counts[bucket] = reader.GetInt64(1);
            }
        }

        return counts;
    }

    public async Task<LogEntryDto?> GetEntryAsync(string serverId, long id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {EntryColumns} FROM entries e WHERE e.server_id = $s AND e.id = $id";
        command.Parameters.AddWithValue("$s", serverId);
        command.Parameters.AddWithValue("$id", id);
        return (await ReadEntriesAsync(command, cancellationToken)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<LogEntryDto>> ContextAsync(string serverId, LogEntryDto focus, int before, int after, bool includeNoise,
        IReadOnlyCollection<string> logs, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var focusMs = focus.Timestamp.ToUnixTimeMilliseconds();

        async Task<IReadOnlyList<LogEntryDto>> Side(bool earlier, int count)
        {
            await using var command = connection.CreateCommand();
            var where = new StringBuilder("e.server_id = $s");
            where.Append(earlier
                ? " AND (e.ts < $t OR (e.ts = $t AND e.id < $id))"
                : " AND (e.ts > $t OR (e.ts = $t AND e.id > $id))");
            command.Parameters.AddWithValue("$s", serverId);
            command.Parameters.AddWithValue("$t", focusMs);
            command.Parameters.AddWithValue("$id", focus.Id);
            if (logs.Count > 0)
            {
                AppendIn(where, command, "e.kind", "lg", logs, negate: false);
            }

            if (!includeNoise)
            {
                AppendNoiseFilter(where);
            }

            var order = earlier ? "DESC" : "ASC";
            command.CommandText = $"SELECT {EntryColumns} FROM entries e WHERE {where} ORDER BY e.ts {order}, e.id {order} LIMIT $n";
            command.Parameters.AddWithValue("$n", count);
            return await ReadEntriesAsync(command, cancellationToken);
        }

        var earlierEntries = before > 0 ? await Side(true, before) : [];
        var laterEntries = after > 0 ? await Side(false, after) : [];
        return [.. earlierEntries.Reverse(), focus, .. laterEntries];
    }

    public async Task<IReadOnlyList<SignatureRow>> SignaturesAsync(string serverId, long fromMs, long toMs, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.kind, s.level, s.source, s.template, s.first_ts, s.muted, g.c, g.last_ts,
                   (SELECT message FROM entries WHERE id = g.last_id)
            FROM (SELECT e.signature_id AS sid, COUNT(*) AS c, MAX(e.ts) AS last_ts, MAX(e.id) AS last_id
                  FROM entries e
                  WHERE e.server_id = $s AND e.signature_id IS NOT NULL AND e.ts >= $from AND e.ts < $to
                  GROUP BY e.signature_id) g
            JOIN signatures s ON s.id = g.sid
            """;
        command.Parameters.AddWithValue("$s", serverId);
        command.Parameters.AddWithValue("$from", fromMs);
        command.Parameters.AddWithValue("$to", toMs);
        var rows = new List<SignatureRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SignatureRow(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4), reader.GetInt64(5), reader.GetInt64(6) != 0, reader.GetInt64(7), reader.GetInt64(8),
                reader.IsDBNull(9) ? string.Empty : reader.GetString(9)));
        }

        return rows;
    }

    public async Task<IReadOnlyDictionary<long, long[]>> SignatureTrendsAsync(string serverId, IReadOnlyCollection<long> ids, long fromMs,
        long bucketMs, int buckets, CancellationToken cancellationToken)
    {
        var trends = ids.ToDictionary(id => id, _ => new long[buckets]);
        if (ids.Count == 0)
        {
            return trends;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var where = new StringBuilder("e.server_id = $s AND e.ts >= $from AND e.ts < $to");
        AppendIn(where, command, "e.signature_id", "sg", ids.Cast<object>(), negate: false);
        command.CommandText = $"SELECT e.signature_id, (e.ts - $from) / $bucket, COUNT(*) FROM entries e WHERE {where} GROUP BY 1, 2";
        command.Parameters.AddWithValue("$s", serverId);
        command.Parameters.AddWithValue("$from", fromMs);
        command.Parameters.AddWithValue("$to", fromMs + bucketMs * buckets);
        command.Parameters.AddWithValue("$bucket", bucketMs);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var bucket = reader.GetInt64(1);
            if (trends.TryGetValue(reader.GetInt64(0), out var counts) && bucket >= 0 && bucket < buckets)
            {
                counts[bucket] = reader.GetInt64(2);
            }
        }

        return trends;
    }

    public async Task<bool> SetSignatureMutedAsync(string serverId, long id, bool muted, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE signatures SET muted = $m WHERE server_id = $s AND id = $id";
        command.Parameters.AddWithValue("$m", muted ? 1 : 0);
        command.Parameters.AddWithValue("$s", serverId);
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<long?> LastServerStartAsync(string serverId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MAX(e.ts) FROM entries e
            WHERE e.server_id = $s AND e.kind = 'main' AND e.message = 'Server logger started.'
              AND e.id IN (SELECT rowid FROM entries_fts WHERE entries_fts MATCH '"Server logger started"')
            """;
        command.Parameters.AddWithValue("$s", serverId);
        return await command.ExecuteScalarAsync(cancellationToken) is long value ? value : null;
    }

    public async Task<LogIndexStats> StatsAsync(string serverId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var files = connection.CreateCommand();
        files.CommandText = "SELECT COUNT(*), COALESCE(SUM(offset), 0), COALESCE(SUM(size), 0) FROM log_files WHERE server_id = $s";
        files.Parameters.AddWithValue("$s", serverId);
        int count;
        long indexed, total;
        await using (var reader = await files.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            (count, indexed, total) = (reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2));
        }

        await using var entries = connection.CreateCommand();
        entries.CommandText = "SELECT COUNT(*), MIN(ts), MAX(ts) FROM entries WHERE server_id = $s";
        entries.Parameters.AddWithValue("$s", serverId);
        await using var entryReader = await entries.ExecuteReaderAsync(cancellationToken);
        await entryReader.ReadAsync(cancellationToken);
        return new LogIndexStats(
            entryReader.GetInt64(0), indexed, total, count,
            entryReader.IsDBNull(1) ? null : entryReader.GetInt64(1),
            entryReader.IsDBNull(2) ? null : entryReader.GetInt64(2));
    }

    public long DatabaseBytes() =>
        new[] { databasePath, databasePath + "-wal" }.Where(File.Exists).Sum(path => new FileInfo(path).Length);

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA synchronous = NORMAL";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static StringBuilder BuildWhere(LogFilter filter, SqliteCommand command)
    {
        var query = filter.Query;
        var where = new StringBuilder("e.server_id = $s AND e.ts >= $from AND e.ts < $to");
        command.Parameters.AddWithValue("$s", filter.ServerId);
        command.Parameters.AddWithValue("$from", filter.FromMs);
        command.Parameters.AddWithValue("$to", filter.ToMs);

        AppendIn(where, command, "e.kind", "lg", query.Logs, negate: false);
        AppendIn(where, command, "e.kind", "xlg", query.ExcludedLogs, negate: true);
        AppendIn(where, command, "e.action", "ac", query.Actions, negate: false);
        AppendIn(where, command, "e.action", "xac", query.ExcludedActions, negate: true);
        AppendEquals(where, command, "e.level", "lv", query.Levels, negate: false);
        AppendEquals(where, command, "e.level", "xlv", query.ExcludedLevels, negate: true);
        AppendEquals(where, command, "e.source", "sr", query.Sources, negate: false);
        AppendEquals(where, command, "e.source", "xsr", query.ExcludedSources, negate: true);
        AppendEquals(where, command, "e.actor", "pl", query.Players, negate: false);
        AppendEquals(where, command, "e.actor", "xpl", query.ExcludedPlayers, negate: true);

        if (query.Items.Count > 0)
        {
            where.Append(" AND (");
            for (var i = 0; i < query.Items.Count; i++)
            {
                where.Append(i == 0 ? string.Empty : " OR ").Append($"e.item LIKE $it{i} ESCAPE '\\'");
                command.Parameters.AddWithValue($"$it{i}", "%" + EscapeLike(query.Items[i]) + "%");
            }

            where.Append(')');
        }

        var shortcutIndex = 0;
        foreach (var (key, values) in query.Shortcuts)
        {
            var conditions = new List<string>();
            foreach (var value in values)
            {
                var name = $"$sc{shortcutIndex++}";
                command.Parameters.AddWithValue(name, value);
                conditions.Add(key switch
                {
                    "command" => $"(e.action = 'command' AND e.item LIKE {name} || '%')",
                    "killed" => $"(e.action = 'kill' AND (e.other = {name} COLLATE NOCASE OR e.item LIKE '%' || {name} || '%'))",
                    // A player's death message, or a player kill by that killer (whose victim was resolved from damage).
                    "killedby" => $"((e.action = 'death' AND e.other LIKE {name} || '%') OR (e.action = 'kill' AND e.item = 'game:player' AND e.actor = {name} COLLATE NOCASE))",
                    "with" => $"(e.other = {name} COLLATE NOCASE)",
                    _ => $"(e.action = '{Services.LogQuery.ShortcutActions[key]}' AND e.item LIKE '%' || {name} || '%')"
                });
            }

            where.Append(" AND (").AppendJoin(" OR ", conditions).Append(')');
        }

        if (query.SignatureId is { } signatureId)
        {
            where.Append(" AND e.signature_id = $sig");
            command.Parameters.AddWithValue("$sig", signatureId);
        }

        if (query.Near is { } near)
        {
            where.Append(" AND e.x BETWEEN $nx0 AND $nx1 AND e.z BETWEEN $nz0 AND $nz1");
            command.Parameters.AddWithValue("$nx0", near.X - near.Radius);
            command.Parameters.AddWithValue("$nx1", near.X + near.Radius);
            command.Parameters.AddWithValue("$nz0", near.Z - near.Radius);
            command.Parameters.AddWithValue("$nz1", near.Z + near.Radius);
            if (near.Y is { } y)
            {
                where.Append(" AND e.y BETWEEN $ny0 AND $ny1");
                command.Parameters.AddWithValue("$ny0", y - near.Radius);
                command.Parameters.AddWithValue("$ny1", y + near.Radius);
            }
        }

        // Inventory clicks and "too far away" packets are most of the audit log; show them only when asked.
        if (!filter.IncludeNoise && query.Actions.Count == 0 && query.Shortcuts.Count == 0)
        {
            AppendNoiseFilter(where);
        }

        if (query.ToFtsExpression() is { } fts)
        {
            where.Append(" AND e.id IN (SELECT rowid FROM entries_fts WHERE entries_fts MATCH $fts)");
            command.Parameters.AddWithValue("$fts", fts);
        }

        return where;
    }

    private static void AppendNoiseFilter(StringBuilder where) =>
        where.Append(" AND (e.action IS NULL OR e.action NOT IN (")
            .AppendJoin(", ", AuditParser.NoiseActions.Select(action => $"'{action}'"))
            .Append("))");

    private static void AppendIn(StringBuilder where, SqliteCommand command, string column, string prefix, IEnumerable<object> values, bool negate)
    {
        var list = values.ToArray();
        if (list.Length == 0)
        {
            return;
        }

        var names = list.Select((value, i) =>
        {
            command.Parameters.AddWithValue($"${prefix}{i}", value);
            return $"${prefix}{i}";
        });
        where.Append(negate ? $" AND ({column} IS NULL OR {column} NOT IN (" : $" AND {column} IN (")
            .AppendJoin(", ", names)
            .Append(negate ? "))" : ")");
    }

    private static void AppendIn(StringBuilder where, SqliteCommand command, string column, string prefix, IReadOnlyCollection<string> values, bool negate) =>
        AppendIn(where, command, column, prefix, values.Cast<object>(), negate);

    private static void AppendEquals(StringBuilder where, SqliteCommand command, string column, string prefix, IReadOnlyList<string> values, bool negate)
    {
        if (values.Count == 0)
        {
            return;
        }

        var conditions = values.Select((value, i) =>
        {
            command.Parameters.AddWithValue($"${prefix}{i}", value);
            return $"{column} = ${prefix}{i} COLLATE NOCASE";
        });
        var joined = string.Join(" OR ", conditions);
        where.Append(negate ? $" AND ({column} IS NULL OR NOT ({joined}))" : $" AND ({joined})");
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static async Task<IReadOnlyList<LogEntryDto>> ReadEntriesAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var entries = new List<LogEntryDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            int? Number(int i) => reader.IsDBNull(i) ? null : reader.GetInt32(i);
            entries.Add(new LogEntryDto(
                reader.GetInt64(0),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                reader.GetString(2),
                reader.GetString(3),
                Text(4), Text(5), Text(6), Text(7),
                Number(8), Number(9), Number(10),
                reader.GetString(11),
                Text(12),
                reader.IsDBNull(13) ? null : reader.GetInt64(13),
                Text(14)));
        }

        return entries;
    }
}
