using AlegacyWebPanel.Core.Auditing;
using AlegacyWebPanel.Modules.Audit.Contracts;
using AlegacyWebPanel.Modules.Audit.Exceptions;
using AlegacyWebPanel.Modules.Audit.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AlegacyWebPanel.Audit.UnitTests;

public sealed class AuditRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Added_events_are_returned_newest_first_with_all_fields()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Repository.AddAsync(Event("alice", "files", "delete", target: "Mods/a.zip", serverId: "main"), Now.AddMinutes(-5), default);
        await fixture.Repository.AddAsync(Event("bob", "server", "restart", serverId: "main", details: "{\"x\":1}"), Now, default);

        var result = await fixture.Repository.QueryAsync(new AuditQuery(), default);

        Assert.Equal(2, result.Total);
        Assert.Equal("bob", result.Items[0].Actor);
        var older = result.Items[1];
        Assert.Equal("alice", older.Actor);
        Assert.Equal("Admin", older.ActorRole);
        Assert.Equal("10.0.0.1", older.IpAddress);
        Assert.Equal("Mods/a.zip", older.Target);
        Assert.Equal(Now.AddMinutes(-5), older.TimestampUtc);
        Assert.True(older.Succeeded);
    }

    [Fact]
    public async Task Query_filters_by_actor_category_action_server_and_outcome()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Repository.AddAsync(Event("alice", "files", "delete", serverId: "main"), Now, default);
        await fixture.Repository.AddAsync(Event("alice", "files", "upload", serverId: "test"), Now, default);
        await fixture.Repository.AddAsync(Event("bob", "server", "restart", serverId: "main", succeeded: false, error: "409"), Now, default);

        Assert.Equal(2, (await fixture.Repository.QueryAsync(new AuditQuery(Actor: "alice"), default)).Total);
        Assert.Equal(2, (await fixture.Repository.QueryAsync(new AuditQuery(Category: "files"), default)).Total);
        Assert.Equal(1, (await fixture.Repository.QueryAsync(new AuditQuery(Action: "upload"), default)).Total);
        Assert.Equal(2, (await fixture.Repository.QueryAsync(new AuditQuery(ServerId: "main"), default)).Total);
        var failed = await fixture.Repository.QueryAsync(new AuditQuery(Succeeded: false), default);
        Assert.Equal("bob", Assert.Single(failed.Items).Actor);
    }

    [Fact]
    public async Task Query_filters_by_time_range_and_searches_target_details_and_error()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Repository.AddAsync(Event("alice", "files", "delete", target: "Saves/world.vcdbs"), Now.AddDays(-2), default);
        await fixture.Repository.AddAsync(Event("alice", "server", "command", details: "{\"command\":\"/stats\"}"), Now.AddDays(-1), default);
        await fixture.Repository.AddAsync(Event("bob", "files", "upload", succeeded: false, error: "Permission denied"), Now, default);

        var range = await fixture.Repository.QueryAsync(new AuditQuery(From: Now.AddDays(-1.5), To: Now.AddHours(-1)), default);
        Assert.Equal("command", Assert.Single(range.Items).Action);

        Assert.Equal("delete", Assert.Single((await fixture.Repository.QueryAsync(new AuditQuery(Search: "world.vcdbs"), default)).Items).Action);
        Assert.Equal("command", Assert.Single((await fixture.Repository.QueryAsync(new AuditQuery(Search: "/stats"), default)).Items).Action);
        Assert.Equal("upload", Assert.Single((await fixture.Repository.QueryAsync(new AuditQuery(Search: "denied"), default)).Items).Action);
    }

    [Fact]
    public async Task Query_pages_results_and_reports_the_total()
    {
        await using var fixture = await CreateFixtureAsync();
        for (var i = 0; i < 5; i++)
        {
            await fixture.Repository.AddAsync(Event("alice", "files", "save", target: $"f{i}"), Now.AddMinutes(i), default);
        }

        var page = await fixture.Repository.QueryAsync(new AuditQuery(Limit: 2, Offset: 2), default);

        Assert.Equal(5, page.Total);
        Assert.Equal(["f2", "f1"], page.Items.Select(e => e.Target));
    }

    [Fact]
    public async Task Facets_list_distinct_actors_actions_and_servers()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Repository.AddAsync(Event("bob", "server", "restart", serverId: "main"), Now, default);
        await fixture.Repository.AddAsync(Event("alice", "files", "delete", serverId: "main"), Now, default);
        await fixture.Repository.AddAsync(Event("alice", "files", "delete", serverId: "test"), Now, default);
        await fixture.Repository.AddAsync(Event("alice", "users", "create"), Now, default);

        var facets = await fixture.Repository.GetFacetsAsync(default);

        Assert.Equal(["alice", "bob"], facets.Actors);
        Assert.Equal(["main", "test"], facets.Servers);
        Assert.Equal(
            [("files", "delete"), ("server", "restart"), ("users", "create")],
            facets.Actions.Select(a => (a.Category, a.Action)));
    }

    [Fact]
    public async Task Prune_removes_only_entries_older_than_the_cutoff()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Repository.AddAsync(Event("alice", "files", "delete", target: "old"), Now.AddDays(-400), default);
        await fixture.Repository.AddAsync(Event("alice", "files", "delete", target: "new"), Now.AddDays(-10), default);

        var deleted = await fixture.Repository.PruneAsync(Now.AddDays(-365), default);

        Assert.Equal(1, deleted);
        Assert.Equal("new", Assert.Single((await fixture.Repository.QueryAsync(new AuditQuery(), default)).Items).Target);
    }

    [Fact]
    public async Task Storage_failures_are_translated_to_unavailable()
    {
        var closedConnection = new SqliteConnection("DataSource=:memory:");
        await closedConnection.OpenAsync();
        await closedConnection.DisposeAsync();
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(closedConnection).Options;
        var repository = new SqliteAuditRepository(new TestContextFactory(options));

        await Assert.ThrowsAsync<AuditStoreUnavailableException>(() => repository.QueryAsync(new AuditQuery(), default));
    }

    private static AuditEvent Event(
        string actor, string category, string action, string? serverId = null, string? target = null,
        string? details = null, bool succeeded = true, string? error = null) =>
        new(actor, "Admin", "10.0.0.1", category, action, serverId, target, details, succeeded, error);

    // The repository creates the schema on first use, so no set-up is needed here.
    private static async Task<Fixture> CreateFixtureAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(connection).Options;
        return new Fixture(connection, new SqliteAuditRepository(new TestContextFactory(options)));
    }

    private sealed class Fixture(SqliteConnection connection, SqliteAuditRepository repository) : IAsyncDisposable
    {
        public SqliteAuditRepository Repository { get; } = repository;

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class TestContextFactory(DbContextOptions<AuditDbContext> options) : IDbContextFactory<AuditDbContext>
    {
        public AuditDbContext CreateDbContext() => new(options);

        public Task<AuditDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
