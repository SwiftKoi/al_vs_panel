using AlegacyWebPanel.Modules.Logging.Contracts;
using AlegacyWebPanel.Modules.Logging.Exceptions;
using AlegacyWebPanel.Modules.Logging.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlegacyWebPanel.Logging.UnitTests;

public sealed class SqliteLogRepositoryTests
{
    [Fact]
    public async Task Query_returns_persisted_entries_with_ids()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Repository.WriteAsync(
            [Entry("Information", "Alegacy.Test", "started"), Entry("Error", "Alegacy.Test", "failed")],
            CancellationToken.None);

        var result = await fixture.Repository.QueryAsync(new LogQuery(), CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.NotEqual(0, result.Entries[0].Id);
        Assert.Contains(result.Entries, entry => entry.Message == "failed");
    }

    [Fact]
    public async Task Query_filters_by_minimum_level()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Repository.WriteAsync(
            [
                Entry("Information", "Alegacy.Test", "info"),
                Entry("Warning", "Alegacy.Test", "warn"),
                Entry("Error", "Alegacy.Test", "error"),
                Entry("Critical", "Alegacy.Test", "critical")
            ],
            CancellationToken.None);

        var result = await fixture.Repository.QueryAsync(
            new LogQuery(MinimumLevel: LogLevel.Error), CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.All(result.Entries, entry => Assert.True(entry.Level is "Error" or "Critical"));
    }

    [Fact]
    public async Task Query_filters_by_source()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Repository.WriteAsync(
            [
                Entry("Information", "Alegacy.Test", "one"),
                Entry("Information", "Alegacy.Other", "two")
            ],
            CancellationToken.None);

        var result = await fixture.Repository.QueryAsync(
            new LogQuery(Source: "Alegacy.Test"), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("Alegacy.Test", result.Entries[0].Category);
    }

    [Fact]
    public async Task Query_searches_message_text()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Repository.WriteAsync(
            [
                Entry("Information", "Alegacy.Test", "disk is full"),
                Entry("Information", "Alegacy.Test", "all good")
            ],
            CancellationToken.None);

        var result = await fixture.Repository.QueryAsync(
            new LogQuery(Search: "disk"), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("disk is full", result.Entries[0].Message);
    }

    [Fact]
    public async Task Query_filters_by_time_range()
    {
        await using var fixture = await CreateFixtureAsync();
        var now = DateTimeOffset.UtcNow;
        await fixture.Repository.WriteAsync(
            [
                Entry("Information", "Alegacy.Test", "old", now.AddMinutes(-5)),
                Entry("Information", "Alegacy.Test", "current", now)
            ],
            CancellationToken.None);

        var result = await fixture.Repository.QueryAsync(
            new LogQuery(From: now.AddMinutes(-2), To: now.AddMinutes(1)), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("current", result.Entries[0].Message);
    }

    [Fact]
    public async Task Query_orders_newest_first_and_applies_pagination()
    {
        await using var fixture = await CreateFixtureAsync();
        var now = DateTimeOffset.UtcNow;
        await fixture.Repository.WriteAsync(
            [
                Entry("Information", "Alegacy.Test", "first", now.AddMinutes(-2)),
                Entry("Information", "Alegacy.Test", "second", now.AddMinutes(-1)),
                Entry("Information", "Alegacy.Test", "third", now)
            ],
            CancellationToken.None);

        var page = await fixture.Repository.QueryAsync(
            new LogQuery(Limit: 2, Offset: 1), CancellationToken.None);

        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Entries.Count);
        Assert.Equal("second", page.Entries[0].Message);
        Assert.Equal("first", page.Entries[1].Message);
    }

    [Fact]
    public async Task ListSources_returns_distinct_sorted_categories()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Repository.WriteAsync(
            [
                Entry("Information", "B.Category", "one"),
                Entry("Information", "A.Category", "two"),
                Entry("Information", "B.Category", "three")
            ],
            CancellationToken.None);

        var sources = await fixture.Repository.ListSourcesAsync(CancellationToken.None);

        Assert.Equal(["A.Category", "B.Category"], sources);
    }

    [Fact]
    public async Task CountByLevel_groups_counts_within_range()
    {
        await using var fixture = await CreateFixtureAsync();
        var now = DateTimeOffset.UtcNow;
        await fixture.Repository.WriteAsync(
            [
                Entry("Information", "Alegacy.Test", "one", now),
                Entry("Information", "Alegacy.Test", "two", now),
                Entry("Error", "Alegacy.Test", "three", now),
                Entry("Warning", "Alegacy.Test", "four", now.AddMinutes(-10))
            ],
            CancellationToken.None);

        var counts = await fixture.Repository.CountByLevelAsync(
            now.AddMinutes(-1), now.AddMinutes(1), CancellationToken.None);

        Assert.Equal(2, counts.Information);
        Assert.Equal(1, counts.Error);
        Assert.Equal(0, counts.Warning);
        Assert.Equal(0, counts.Critical);
        Assert.Equal(0, counts.Debug);
    }

    [Fact]
    public async Task Delete_removes_only_entries_older_than_requested_days()
    {
        await using var fixture = await CreateFixtureAsync();
        var now = DateTimeOffset.UtcNow;
        await fixture.Repository.WriteAsync(
            [
                Entry("Information", "Alegacy.Test", "old", now.AddDays(-5)),
                Entry("Information", "Alegacy.Test", "new", now)
            ],
            CancellationToken.None);

        var deleted = await fixture.Repository.DeleteAsync(1, CancellationToken.None);

        Assert.Equal(1, deleted);
        var result = await fixture.Repository.QueryAsync(new LogQuery(), CancellationToken.None);
        Assert.Equal(1, result.Total);
        Assert.Equal("new", result.Entries[0].Message);
    }

    [Fact]
    public async Task Delete_without_filter_removes_everything()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.Repository.WriteAsync(
            [Entry("Information", "Alegacy.Test", "one"), Entry("Error", "Alegacy.Test", "two")],
            CancellationToken.None);

        var deleted = await fixture.Repository.DeleteAsync(null, CancellationToken.None);

        Assert.Equal(2, deleted);
        var result = await fixture.Repository.QueryAsync(new LogQuery(), CancellationToken.None);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task Prune_trims_entries_beyond_retained_count()
    {
        await using var fixture = await CreateFixtureAsync();
        var now = DateTimeOffset.UtcNow;
        await fixture.Repository.WriteAsync(
            [
                Entry("Information", "Alegacy.Test", "oldest", now.AddMinutes(-5)),
                Entry("Information", "Alegacy.Test", "older", now.AddMinutes(-4)),
                Entry("Information", "Alegacy.Test", "middle", now.AddMinutes(-3)),
                Entry("Information", "Alegacy.Test", "newer", now.AddMinutes(-2)),
                Entry("Information", "Alegacy.Test", "newest", now.AddMinutes(-1))
            ],
            CancellationToken.None);

        await fixture.Repository.PruneAsync(maximumRetainedDays: 30, maximumRetainedEntries: 2, CancellationToken.None);

        var result = await fixture.Repository.QueryAsync(new LogQuery(), CancellationToken.None);
        Assert.Equal(2, result.Total);
        Assert.All(result.Entries, entry => Assert.True(entry.Message is "newest" or "newer"));
    }

    [Fact]
    public async Task Prune_removes_entries_older_than_retained_days()
    {
        await using var fixture = await CreateFixtureAsync();
        var now = DateTimeOffset.UtcNow;
        await fixture.Repository.WriteAsync(
            [
                Entry("Information", "Alegacy.Test", "expired", now.AddDays(-10)),
                Entry("Information", "Alegacy.Test", "fresh", now)
            ],
            CancellationToken.None);

        await fixture.Repository.PruneAsync(maximumRetainedDays: 1, maximumRetainedEntries: 1000, CancellationToken.None);

        var result = await fixture.Repository.QueryAsync(new LogQuery(), CancellationToken.None);
        Assert.Equal(1, result.Total);
        Assert.Equal("fresh", result.Entries[0].Message);
    }

    [Fact]
    public async Task Storage_failures_are_translated_to_unavailable()
    {
        var closedConnection = new SqliteConnection("DataSource=:memory:");
        await closedConnection.OpenAsync();
        await closedConnection.DisposeAsync();

        var options = new DbContextOptionsBuilder<LogDbContext>().UseSqlite(closedConnection).Options;
        var fixture = new SqliteRepositoryFixture(closedConnection, options);

        await Assert.ThrowsAsync<LogStoreUnavailableException>(() =>
            fixture.Repository.QueryAsync(new LogQuery(), CancellationToken.None));
    }

    private static LogEntry Entry(string level, string category, string message, DateTimeOffset? timestamp = null) =>
        new(0, timestamp ?? DateTimeOffset.UtcNow, level, category, 0, message, null, null, null, null);

    private static async Task<SqliteRepositoryFixture> CreateFixtureAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LogDbContext>().UseSqlite(connection).Options;
        await using (var db = new LogDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        return new SqliteRepositoryFixture(connection, options);
    }

    private sealed class SqliteRepositoryFixture(SqliteConnection connection, DbContextOptions<LogDbContext> options)
        : IAsyncDisposable
    {
        public SqliteLogRepository Repository { get; } = new(new TestContextFactory(options));

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class TestContextFactory(DbContextOptions<LogDbContext> options) : IDbContextFactory<LogDbContext>
    {
        public LogDbContext CreateDbContext() => new(options);

        public Task<LogDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
