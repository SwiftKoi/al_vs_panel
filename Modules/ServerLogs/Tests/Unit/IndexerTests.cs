using System.Text;
using AlegacyWebPanel.Modules.ServerLogs.Configuration;
using AlegacyWebPanel.Modules.ServerLogs.Persistence;
using AlegacyWebPanel.Modules.ServerLogs.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AlegacyWebPanel.ServerLogs.UnitTests;

public sealed class IndexerTests : IAsyncLifetime
{
    private const string Server = "main";
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"serverlogs-{Guid.NewGuid():N}.db");
    private readonly FakeSource _source = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly ServerLogsOptions _options = new()
    {
        DatabasePath = "unused",
        ReadChunkBytes = 64 * 1024,
        MaximumBytesPerPass = 1024 * 1024,
        Servers = { [Server] = new ServerLogsServerOptions { Operation = "logs" } }
    };

    private SqliteLogIndexRepository _index = null!;
    private LogIndexer _indexer = null!;
    private ServerLogService _service = null!;

    public async Task InitializeAsync()
    {
        _index = new SqliteLogIndexRepository(_databasePath);
        await _index.InitializeAsync(CancellationToken.None);
        _indexer = new LogIndexer(_source, _index, Options.Create(_options), _time, NullLogger<LogIndexer>.Instance);
        _service = new ServerLogService(_index, new LogIndexState(), Options.Create(_options), _time);
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_databasePath + suffix);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Only_new_bytes_are_read_and_a_partial_last_line_waits()
    {
        _source.Files["server-main.log"] = "28.9.2026 10:00:00 [Notification] Server logger started.\n28.9.2026 10:00:01 [Error] Boom";
        await IndexAsync();
        Assert.Single(await SearchAsync(""));

        _source.Files["server-main.log"] += " happened\n   at Stack.Trace()\n28.9.2026 10:00:02 [Warning] later\n";
        await IndexAsync();

        var entries = await SearchAsync("");
        Assert.Equal(["later", "Boom happened", "Server logger started."], entries.Select(e => e.Message));
        Assert.Equal("   at Stack.Trace()", entries[1].Extra);
        Assert.Equal(_source.Files["server-main.log"].Length, _source.BytesRead);
    }

    [Fact]
    public async Task Continuation_arriving_in_the_next_pass_is_appended_to_the_previous_entry()
    {
        _source.Files["server-main.log"] = "28.9.2026 10:00:01 [Error] Boom\n";
        await IndexAsync();
        _source.Files["server-main.log"] += "   at Stack.Trace()\n";
        await IndexAsync();

        var entry = Assert.Single(await SearchAsync("trace"));
        Assert.Equal("   at Stack.Trace()", entry.Extra);
    }

    [Fact]
    public async Task A_rotated_file_is_recognised_by_its_first_line_and_not_read_again()
    {
        const string first = "28.9.2026 09:00:00 [Notification] Server logger started.\n";
        _source.Files["server-main.log"] = first;
        await IndexAsync();

        // The game moves the file to the archive (with a last line we have not seen yet) and starts a new one.
        _source.Files.Remove("server-main.log");
        _source.Files["Archive/2026-09-28_22_00_06/server-main.log"] = first + "28.9.2026 09:59:59 [Event] last words\n";
        _source.Files["server-main.log"] = "28.9.2026 10:00:00 [Notification] Server logger started.\n";
        _source.BytesRead = 0;
        await IndexAsync();

        Assert.Equal(3, (await SearchAsync("")).Count);
        Assert.Equal("28.9.2026 09:59:59 [Event] last words\n".Length + _source.Files["server-main.log"].Length, _source.BytesRead);
    }

    [Fact]
    public async Task Entries_past_retention_are_skipped_and_pruned()
    {
        _options.RetentionDays["main"] = 1;
        _source.Files["server-main.log"] = "20.9.2026 10:00:00 [Event] too old\n28.9.2026 11:00:00 [Event] recent\n";
        await IndexAsync();
        var all = await _service.SearchAsync(Server, new LogSearchRequest(null, _time.GetUtcNow().AddDays(-30), null, false), null, null, CancellationToken.None);
        Assert.Equal(["recent"], all.Entries.Select(e => e.Message));

        _time.Advance(TimeSpan.FromDays(2));
        await _indexer.PruneAsync(Server, CancellationToken.None);
        var after = await _service.SearchAsync(Server, new LogSearchRequest(null, _time.GetUtcNow().AddDays(-30), null, false), null, null, CancellationToken.None);
        Assert.Empty(after.Entries);
    }

    [Fact]
    public async Task Search_filters_facets_context_and_noise()
    {
        _source.Files["server-audit.log"] = string.Join('\n',
            "28.9.2026 10:00:00 [Audit] Orex_ Put 8xgame:firewood into Ground storage at 100, 64, 200.",
            "28.9.2026 10:00:01 [Audit] Orex_ left clicked slot 1 in backpack-x. Before: (mouse: empty)",
            "28.9.2026 10:00:02 [Audit] [RAC] RiddlE9 Took 2xgame:ingot-iron from Crate at 5000, 64, 5000.",
            "28.9.2026 10:00:03 [Audit] command for Orex_ /land claim load 1",
            "");
        await IndexAsync();

        Assert.Equal(3, (await SearchAsync("")).Count); // the click is noise
        Assert.Equal(4, (await SearchAsync("", noise: true)).Count);
        Assert.Equal(["command", "put"], (await SearchAsync("player:orex_")).Select(e => e.Action));
        Assert.Equal("RiddlE9", Assert.Single(await SearchAsync("near:5010,4990~20")).Player);
        Assert.Equal("game:firewood", Assert.Single(await SearchAsync("item:firewood")).Item);
        Assert.Single(await SearchAsync("firew"));
        Assert.Single(await SearchAsync("action:command"));
        Assert.Equal(2, (await SearchAsync("-player:RiddlE9")).Count);

        var facets = await _service.FacetsAsync(Server, Request(""), CancellationToken.None);
        Assert.Equal(3, facets.Total);
        Assert.Equal(2, facets.Players.Single(p => p.Value == "Orex_").Count);

        var focus = (await SearchAsync("action:take")).Single();
        var context = await _service.ContextAsync(Server, focus.Id, 5, 5, includeNoise: true, null, CancellationToken.None);
        Assert.Equal(4, context.Entries.Count);
        Assert.Equal(focus.Id, context.Entries[2].Id);
    }

    [Fact]
    public async Task Paging_uses_a_cursor()
    {
        _source.Files["server-main.log"] = string.Concat(Enumerable.Range(0, 5).Select(i => $"28.9.2026 10:00:0{i} [Event] line {i}\n"));
        await IndexAsync();

        var first = await _service.SearchAsync(Server, Request(""), null, 2, CancellationToken.None);
        var second = await _service.SearchAsync(Server, Request(""), first.NextCursor, 2, CancellationToken.None);
        var third = await _service.SearchAsync(Server, Request(""), second.NextCursor, 2, CancellationToken.None);

        Assert.Equal(["line 4", "line 3", "line 2", "line 1", "line 0"], first.Entries.Concat(second.Entries).Concat(third.Entries).Select(e => e.Message));
        Assert.Null(third.NextCursor);
    }

    [Fact]
    public async Task Repeated_warnings_group_into_signatures_marked_new_after_the_last_start()
    {
        _source.Files["Archive/a/server-main.log"] = string.Join('\n',
            "28.9.2026 08:00:00 [Notification] Server logger started.",
            "28.9.2026 08:00:01 [Warning] Server overloaded. A tick took 1200ms to complete.",
            "");
        _source.Files["server-main.log"] = string.Join('\n',
            "28.9.2026 10:00:00 [Notification] Server logger started.",
            "28.9.2026 10:00:01 [Warning] Server overloaded. A tick took 900ms to complete.",
            "28.9.2026 10:00:02 [Error] At position 1, 2, 3 for block x threw an error",
            "28.9.2026 10:00:03 [Error] At position 4, 5, 6 for block x threw an error",
            "");
        await IndexAsync();

        var result = await _service.SignaturesAsync(Server, null, null, CancellationToken.None);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero), result.LastServerStart);
        var error = result.Signatures[0];
        Assert.Equal("At position <pos> for block x threw an error", error.Template);
        Assert.Equal(2, error.Count);
        Assert.True(error.IsNew);
        var overload = result.Signatures.Single(s => s.Level == "Warning");
        Assert.Equal(2, overload.Count);
        Assert.False(overload.IsNew);

        await _service.SetSignatureMutedAsync(Server, error.Id, true, CancellationToken.None);
        Assert.True((await _service.SignaturesAsync(Server, null, null, CancellationToken.None)).Signatures[^1].Muted);
        Assert.Equal(2, (await SearchAsync($"sig:{error.Id}")).Count);
    }

    [Fact]
    public async Task Export_writes_csv_with_formula_protection()
    {
        _source.Files["server-audit.log"] = "28.9.2026 10:00:03 [Audit] command for Orex_ /land claim load 1\n";
        _source.Files["server-main.log"] = "28.9.2026 10:00:04 [Event] =cmd, \"quoted\"\n";
        await IndexAsync();

        using var output = new MemoryStream();
        await _service.ExportAsync(Server, Request(""), "csv", output, CancellationToken.None);
        var lines = Encoding.UTF8.GetString(output.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.EndsWith(",\"'=cmd, \"\"quoted\"\"\"", lines[1]);
    }

    private Task IndexAsync() => _indexer.IndexAsync(Server, _options.Servers[Server], CancellationToken.None);

    private LogSearchRequest Request(string query, bool noise = false) =>
        new(query, _time.GetUtcNow().AddDays(-1), _time.GetUtcNow().AddDays(1), noise);

    private async Task<IReadOnlyList<Modules.ServerLogs.Contracts.LogEntryDto>> SearchAsync(string query, bool noise = false) =>
        (await _service.SearchAsync(Server, Request(query, noise), null, 500, CancellationToken.None)).Entries;

    private sealed class FakeSource : ILogSourceRepository
    {
        public Dictionary<string, string> Files { get; } = [];
        public long BytesRead { get; set; }

        public Task<IReadOnlyList<LogFileInfo>> ListAsync(string operation, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LogFileInfo>>(Files.Select(file =>
            {
                var bytes = Encoding.UTF8.GetBytes(file.Value);
                var newline = Array.IndexOf(bytes, (byte)'\n');
                var identity = newline < 0 ? null : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes.AsSpan(0, newline)));
                return new LogFileInfo(file.Key, bytes.Length, DateTimeOffset.UnixEpoch, identity);
            }).ToArray());

        public Task<byte[]> ReadAsync(string operation, string path, long offset, int maximumBytes, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(Files[path]).AsSpan((int)offset);
            var slice = bytes[..Math.Min(bytes.Length, maximumBytes)];
            var end = slice.LastIndexOf((byte)'\n');
            var result = end < 0 ? (slice.Length < maximumBytes ? [] : slice.ToArray()) : slice[..(end + 1)].ToArray();
            BytesRead += result.Length;
            return Task.FromResult(result);
        }
    }
}
