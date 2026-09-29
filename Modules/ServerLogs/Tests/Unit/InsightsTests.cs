using System.Text;
using AlegacyWebPanel.Modules.ServerLogs.Configuration;
using AlegacyWebPanel.Modules.ServerLogs.Contracts;
using AlegacyWebPanel.Modules.ServerLogs.Exceptions;
using AlegacyWebPanel.Modules.ServerLogs.Persistence;
using AlegacyWebPanel.Modules.ServerLogs.Services;
using AlegacyWebPanel.Modules.ServerLogs.Services.Parsing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AlegacyWebPanel.ServerLogs.UnitTests;

public sealed class InsightsTests : IAsyncLifetime
{
    private const string Server = "main";
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"serverlogs-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly ServerLogsOptions _options = new() { Servers = { [Server] = new ServerLogsServerOptions { Operation = "logs" } } };
    private SqliteLogIndexRepository _repository = null!;
    private ServerLogInsightsService _service = null!;

    public async Task InitializeAsync()
    {
        _repository = new SqliteLogIndexRepository(_databasePath);
        await _repository.InitializeAsync(CancellationToken.None);
        _service = new ServerLogInsightsService(_repository, _repository, Options.Create(_options), _time);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_databasePath + suffix);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Player_activity_sums_quantities_and_lists_commands()
    {
        await IndexAsync("server-audit.log",
            "28.9.2026 10:00:00 [Audit] Orex_ joined.",
            "28.9.2026 10:01:00 [Audit] Orex_ Took 16xgame:ingot-iron from Crate at 100, 64, 200.",
            "28.9.2026 10:02:00 [Audit] Orex_ Took 4xgame:ingot-iron from Crate at 101, 64, 201.",
            "28.9.2026 10:03:00 [Audit] Orex_ Put 8xgame:firewood into Ground storage at 5000, 64, 5000.",
            "28.9.2026 10:04:00 [Audit] command for Orex_ /land claim load 1",
            "28.9.2026 10:05:00 [Audit] [RAC] Orex_ killed game:drifter-normal at 100, 64, 200",
            "28.9.2026 10:06:00 [Audit] Orex_ left clicked slot 1 in backpack-x. Before: (mouse: empty)",
            "28.9.2026 10:07:00 [Audit] Rejected player position update for Orex_. Client sent 1,2,3, server pos was XYZ");

        var activity = await _service.PlayerActivityAsync(Server, "orex_", null, null, CancellationToken.None);

        Assert.Equal(new ItemTotalDto("game:ingot-iron", 20, 2), Assert.Single(activity.Taken));
        Assert.Equal(8, Assert.Single(activity.Put).Quantity);
        Assert.Equal("game:drifter-normal", Assert.Single(activity.Kills).Value);
        Assert.Equal("/land", Assert.Single(activity.Commands).Item);
        Assert.Equal("join", Assert.Single(activity.Sessions).Action);
        Assert.Equal(1, activity.Actions.Single(a => a.Value == "click").Count);
        var day = Assert.Single(activity.Days);
        Assert.Equal(6, day.Actions); // everything except the click and the rejected position
        Assert.Equal(1, day.RejectedPositions);
        Assert.Equal(new[] { 3L, 1L }, activity.Places.Select(p => p.Count)); // the crate spot (2 takes + kill), then the firewood

        var player = Assert.Single((await _service.PlayersAsync(Server, null, null, CancellationToken.None)).Players);
        Assert.Equal(("Orex_", 1L, 1L, 1L), (player.Name, player.Commands, player.Kills, player.RejectedPositions));
    }

    [Fact]
    public async Task Location_groups_who_was_there()
    {
        await IndexAsync("server-audit.log",
            "28.9.2026 10:00:00 [Audit] Orex_ Took 16xgame:ingot-iron from Crate at 100, 64, 200.",
            "28.9.2026 11:00:00 [Audit] Missgame Put 2xgame:ingot-iron into Crate at 102, 64, 199.",
            "28.9.2026 11:30:00 [Audit] Missgame Took 1xgame:ingot-iron from Crate at 102, 64, 199.",
            "28.9.2026 11:40:00 [Audit] Faraway Took 1xgame:stick from Crate at 900, 64, 900.");

        var location = await _service.LocationAsync(Server, 100, null, 200, 5, null, null, CancellationToken.None);

        Assert.Equal(["Missgame", "Orex_"], location.Players.Select(p => p.Name));
        Assert.Equal(2, location.Players[0].Count);
        Assert.Equal(17, Assert.Single(location.Taken).Quantity);
        await Assert.ThrowsAsync<LogQueryException>(() => _service.LocationAsync(Server, 0, null, 0, 5000, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Boots_report_version_startup_time_state_and_mod_changes()
    {
        await IndexAsync("Archive/a/server-main.log",
            "27.9.2026 22:00:00 [Notification] Server logger started.",
            "27.9.2026 22:00:00 [Notification] Game Version: v1.22.6 (Stable) by x",
            "27.9.2026 22:00:02 [Notification] Loaded mods (2):",
            "ModID                      Version       Name",
            "alpha                      1.0.0         Alpha",
            "beta                       2.0.0         Beta",
            "27.9.2026 22:00:30 [Notification] Entering runphase GameReady",
            "27.9.2026 23:00:00 [Event] Stopped the server!");
        await IndexAsync("server-main.log",
            "28.9.2026 08:00:00 [Notification] Server logger started.",
            "28.9.2026 08:00:00 [Notification] Game Version: v1.22.7 (Stable) by x",
            "28.9.2026 08:00:02 [Notification] Loaded mods (2):",
            "ModID                      Version       Name",
            "alpha                      1.1.0         Alpha",
            "gamma                      0.1.0         Gamma",
            "28.9.2026 08:00:05 [Warning] something odd",
            "28.9.2026 08:00:40 [Notification] Entering runphase GameReady",
            "28.9.2026 09:00:00 [Error] later error, not during startup");

        var boots = (await _service.BootsAsync(Server, null, null, CancellationToken.None)).Boots;

        Assert.Equal(2, boots.Count);
        var latest = boots[0];
        Assert.Equal(("running", "1.22.7", 2, 40.0, 1L, 0L), (latest.State, latest.GameVersion, latest.ModCount, latest.StartupSeconds, latest.StartupWarnings, latest.StartupErrors));
        Assert.Equal(
            [new ModChangeDto("gamma", "added", null, "0.1.0"), new ModChangeDto("beta", "removed", "2.0.0", null), new ModChangeDto("alpha", "updated", "1.0.0", "1.1.0")],
            latest.ModChanges);
        Assert.Equal("stopped", boots[1].State);
        Assert.Empty(boots[1].ModChanges);
    }

    [Fact]
    public void A_start_without_a_stop_before_the_next_start_is_unclean()
    {
        var boots = BootTimeline.Build([
            new BootMarker(1, "Server logger started.", null),
            new BootMarker(5, "Server logger started.", null),
            new BootMarker(6, "Stopped the server!", null),
            new BootMarker(9, "Server logger started.", null)
        ]);
        Assert.Equal(["unclean", "stopped", "running"], boots.Select(b => b.State));
    }

    [Fact]
    public async Task Problem_summary_counts_new_unmuted_signatures_since_the_last_start()
    {
        await IndexAsync("Archive/a/server-main.log",
            "28.9.2026 06:00:00 [Notification] Server logger started.",
            "28.9.2026 06:00:01 [Warning] old warning 1");
        await IndexAsync("server-main.log",
            "28.9.2026 08:00:00 [Notification] Server logger started.",
            "28.9.2026 08:00:01 [Warning] old warning 2",
            "28.9.2026 08:00:02 [Error] brand new error",
            "28.9.2026 08:00:03 [Warning] brand new warning");

        var summary = await _service.ProblemSummaryAsync(Server, CancellationToken.None);
        Assert.Equal((1L, 1L), (summary.NewErrors, summary.NewWarnings));
        Assert.False((await _service.ProblemSummaryAsync("other", CancellationToken.None)).Configured);
    }

    [Fact]
    public async Task Saved_searches_are_per_user_validated_and_limited()
    {
        var saved = await _service.SaveSearchAsync("u1", Server, new SaveSearchRequest(" Errors ", "level:error", "7d"), CancellationToken.None);
        Assert.Equal("Errors", saved.Name);
        Assert.Single(await _service.SavedSearchesAsync("u1", Server, CancellationToken.None));
        Assert.Empty(await _service.SavedSearchesAsync("u2", Server, CancellationToken.None));

        await Assert.ThrowsAsync<SavedSearchNotFoundException>(() => _service.DeleteSavedSearchAsync("u2", Server, saved.Id, CancellationToken.None));
        await Assert.ThrowsAsync<LogQueryException>(() => _service.SaveSearchAsync("u1", Server, new SaveSearchRequest("x", "near:bad", "7d"), CancellationToken.None));
        await Assert.ThrowsAsync<LogQueryException>(() => _service.SaveSearchAsync("u1", Server, new SaveSearchRequest("x", "a", "2y"), CancellationToken.None));

        for (var i = 1; i < ServerLogInsightsService.MaximumSavedSearches; i++)
        {
            await _service.SaveSearchAsync("u1", Server, new SaveSearchRequest($"s{i}", "a", "24h"), CancellationToken.None);
        }

        await Assert.ThrowsAsync<LogQueryException>(() => _service.SaveSearchAsync("u1", Server, new SaveSearchRequest("one more", "a", "24h"), CancellationToken.None));
        await _service.DeleteSavedSearchAsync("u1", Server, saved.Id, CancellationToken.None);
    }

    [Fact]
    public async Task Existing_databases_get_the_quantity_column_backfilled()
    {
        var legacyPath = Path.Combine(Path.GetTempPath(), $"serverlogs-legacy-{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={legacyPath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE entries (id INTEGER PRIMARY KEY, server_id TEXT NOT NULL, file_id INTEGER NOT NULL, ts INTEGER NOT NULL,
                        kind TEXT NOT NULL, level TEXT NOT NULL, source TEXT NULL, actor TEXT NULL, action TEXT NULL, item TEXT NULL,
                        x INTEGER NULL, y INTEGER NULL, z INTEGER NULL, signature_id INTEGER NULL, message TEXT NOT NULL, extra TEXT NULL);
                    INSERT INTO entries (server_id, file_id, ts, kind, level, actor, action, item, message)
                    VALUES ('main', 1, 0, 'audit', 'Audit', 'Orex_', 'take', 'game:stick', 'Orex_ Took 12xgame:stick from Crate at 1, 2, 3.');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await new SqliteLogIndexRepository(legacyPath).InitializeAsync(CancellationToken.None);

            await using var check = new SqliteConnection($"Data Source={legacyPath}");
            await check.OpenAsync();
            await using var query = check.CreateCommand();
            query.CommandText = "SELECT qty FROM entries";
            Assert.Equal(12L, await query.ExecuteScalarAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                File.Delete(legacyPath + suffix);
            }
        }
    }

    [Fact]
    public async Task Suggestions_match_prefixes_and_item_paths_by_frequency()
    {
        await IndexAsync("server-audit.log",
            "28.9.2026 10:00:00 [Audit] Orex_ Took 1xgame:ingot-iron from Crate at 1, 2, 3.",
            "28.9.2026 10:00:01 [Audit] Orex_ Took 1xgame:ingot-copper from Crate at 1, 2, 3.",
            "28.9.2026 10:00:02 [Audit] Orlan Took 1xgame:ingot-iron from Crate at 1, 2, 3.",
            "28.9.2026 10:00:03 [Audit] Missgame Took 1xgame:stick_50% from Crate at 1, 2, 3.");

        Assert.Equal(["Orex_", "Orlan"], (await _service.SuggestAsync(Server, "player", "or", null, null, null, CancellationToken.None)).Values.Select(v => v.Value));
        Assert.Equal(["game:ingot-iron", "game:ingot-copper"], (await _service.SuggestAsync(Server, "item", "ingot", null, null, null, CancellationToken.None)).Values.Select(v => v.Value));
        Assert.Single((await _service.SuggestAsync(Server, "item", "stick_50%", null, null, null, CancellationToken.None)).Values);
        Assert.Empty((await _service.SuggestAsync(Server, "item", "stick%x", null, null, null, CancellationToken.None)).Values);
        await Assert.ThrowsAsync<LogQueryException>(() => _service.SuggestAsync(Server, "message", "x", null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Pvp_kills_name_the_victim_and_shortcuts_find_commands_kills_and_killers()
    {
        await IndexAsync("server-audit.log",
            "23.9.2026 14:13:51 [Audit] nPOCTAK at 505366, 180, 514697 got 5.5/5.5 damage bluntattack ancientarmory:aa-blade by Hikkalibur ♣",
            "23.9.2026 14:13:53 [Audit] Player Hikkalibur ♣ killed game:player at 505366, 180, 514700",
            "23.9.2026 14:13:54 [Audit] Player [AKV] LittleJester killed game:drifter-normal at 1, 2, 3",
            "23.9.2026 14:14:00 [Audit] EDoss умер. Сообщение о смерти: Игрок EDoss убит Пепельный Ползун",
            "23.9.2026 14:15:00 [Audit] Handling command for Hikkalibur /land claim load 1",
            "23.9.2026 14:16:00 [Audit] command for Orex_ /we g s 5",
            "23.9.2026 14:17:00 [Audit] Hikkalibur Took 3xgame:ingot-iron from Crate at 1, 2, 3.");
        var dayAfter = new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
        var search = new ServerLogService(_repository, new LogIndexState(), Options.Create(_options), dayAfter);
        async Task<IReadOnlyList<LogEntryDto>> Find(string query) =>
            (await search.SearchAsync(Server, new LogSearchRequest(query, null, null, false), null, 100, CancellationToken.None)).Entries;

        var kill = Assert.Single(await Find("killed:nPOCTAK"));
        Assert.Equal(("Hikkalibur", "nPOCTAK"), (kill.Player, kill.Other));
        Assert.Single(await Find("killed:drifter"));
        Assert.Equal(2, (await Find("player:Hikkalibur")).Count(e => e.Action is "kill" or "command"));
        Assert.Equal("Пепельный Ползун", Assert.Single(await Find("killedby:Пепельный")).Other);
        Assert.Equal("nPOCTAK", Assert.Single(await Find("killedby:Hikkalibur")).Other);
        Assert.Equal("/land", Assert.Single(await Find("command:land")).Item);
        Assert.Equal(2, (await Find("command:land command:/we")).Count);
        Assert.Single(await Find("took:ingot"));
        Assert.Single(await Find("with:Hikkalibur")); // the damage line, where Hikkalibur is the attacker

        async Task<IEnumerable<string>> Suggest(string key, string prefix, string? context) =>
            (await _service.SuggestAsync(Server, key, prefix, context, null, null, CancellationToken.None)).Values.Select(v => v.Value);
        Assert.Equal(["nPOCTAK"], await Suggest("killed", "", "player:Hikkalibur"));
        Assert.Equal(["/land", "/we"], (await Suggest("command", "", null)).Order());
        Assert.Equal(["/land"], await Suggest("command", "la", null));
        Assert.Equal(["Пепельный Ползун"], await Suggest("killedby", "", null));
    }

    [Fact]
    public void Mod_table_reads_id_and_version_columns() =>
        Assert.Equal(
            new Dictionary<string, string> { ["emojime"] = "1.2.0", ["scaffolding"] = "1.3.1" },
            BootTimeline.ParseModTable(
                "ModID                      Version       Name\nemojime                    1.2.0         EmojiMe      emojime-1.2.0.zip\nscaffolding                1.3.1         Scaffolding"));

    private async Task IndexAsync(string path, params string[] lines)
    {
        var text = string.Join('\n', lines) + "\n";
        var file = await _repository.AddFileAsync(Server, Guid.NewGuid().ToString("N"), LogLineParser.KindFromFileName(path)!, path, CancellationToken.None);
        var chunk = LogLineParser.Parse(file.Kind, text);
        await _repository.AppendAsync(Server, file, Encoding.UTF8.GetByteCount(text), Encoding.UTF8.GetByteCount(text), chunk, 0, CancellationToken.None);
    }
}
