using AlegacyWebPanel.Modules.Authentication.Persistence;
using AlegacyWebPanel.Modules.Users.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AlegacyWebPanel.Authentication.UnitTests;

public sealed class EfLoginEventRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public EfLoginEventRepositoryTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Record_inserts_entry()
    {
        var repository = new EfLoginEventRepository(_db);

        await repository.RecordAsync(new LoginEvent
        {
            Username = "admin",
            IpAddress = "192.168.1.100",
            Succeeded = true,
            OccurredAt = DateTime.UtcNow
        }, CancellationToken.None);

        var count = await _db.LoginEvents.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetPage_returns_newest_first()
    {
        var repository = new EfLoginEventRepository(_db);
        await repository.RecordAsync(Event("first", succeeded: true, DateTime.UtcNow.AddMinutes(-2)), CancellationToken.None);
        await repository.RecordAsync(Event("second", succeeded: false, DateTime.UtcNow.AddMinutes(-1)), CancellationToken.None);
        await repository.RecordAsync(Event("third", succeeded: true, DateTime.UtcNow), CancellationToken.None);

        var page = await repository.GetPageAsync(0, 10, CancellationToken.None);

        Assert.Equal(3, page.Count);
        Assert.Equal("third", page[0].Username);
        Assert.Equal("second", page[1].Username);
        Assert.Equal("first", page[2].Username);
        Assert.True(page[0].Succeeded);
        Assert.False(page[1].Succeeded);
    }

    [Fact]
    public async Task GetPage_respects_offset_and_limit()
    {
        var repository = new EfLoginEventRepository(_db);
        for (var i = 0; i < 5; i++)
        {
            await repository.RecordAsync(Event($"user{i}", succeeded: true, DateTime.UtcNow.AddMinutes(-i)), CancellationToken.None);
        }

        var page = await repository.GetPageAsync(2, 2, CancellationToken.None);

        Assert.Equal(2, page.Count);
        Assert.Equal("user2", page[0].Username);
        Assert.Equal("user3", page[1].Username);
    }

    [Fact]
    public async Task Record_prunes_entries_older_than_retention_window()
    {
        var repository = new EfLoginEventRepository(_db, TimeSpan.FromDays(30));

        await repository.RecordAsync(Event("old", succeeded: true, DateTime.UtcNow.AddDays(-31)), CancellationToken.None);
        await repository.RecordAsync(Event("new", succeeded: true, DateTime.UtcNow), CancellationToken.None);

        var count = await _db.LoginEvents.CountAsync();
        Assert.Equal(1, count);
        var remaining = await _db.LoginEvents.SingleAsync();
        Assert.Equal("new", remaining.Username);
    }

    private static LoginEvent Event(string username, bool succeeded, DateTime occurredAt) => new()
    {
        Username = username,
        IpAddress = "10.0.0.1",
        Succeeded = succeeded,
        OccurredAt = occurredAt
    };
}
