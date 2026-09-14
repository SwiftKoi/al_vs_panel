using System.Threading.Channels;
using AlegacyWebPanel.Modules.Logging.Configuration;
using AlegacyWebPanel.Modules.Logging.Contracts;
using AlegacyWebPanel.Modules.Logging.Infrastructure;
using AlegacyWebPanel.Modules.Logging.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Logging.UnitTests;

public sealed class LogWriteWorkerTests
{
    [Fact]
    public async Task Worker_persists_queued_entries_on_shutdown()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LogDbContext>().UseSqlite(connection).Options;
        await using (var db = new LogDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        var channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        var factory = new TestContextFactory(options);
        var repository = new SqliteLogRepository(factory);
        var log = new List<string>();
        var worker = new TestableWorker(
            channel,
            repository,
            factory,
            Options.Create(new LogStoreOptions()),
            new ListLogger(log),
            new LogDropCounter());

        using var cts = new CancellationTokenSource();
        var runTask = worker.RunAsync(cts.Token);

        await channel.Writer.WriteAsync(new LogEntry(0, DateTimeOffset.UtcNow, "Information", "Cat", 0, "one", null, null, null, null));
        await channel.Writer.WriteAsync(new LogEntry(0, DateTimeOffset.UtcNow, "Error", "Cat", 0, "two", null, null, null, null));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var result = await repository.QueryAsync(new LogQuery(), CancellationToken.None);
            if (result.Total == 2)
            {
                break;
            }

            if (DateTime.UtcNow > deadline)
            {
                cts.Cancel();
                await runTask;
                Assert.Fail($"Worker did not persist queued entries. Fallback log: {string.Join(" | ", log)}");
            }

            await Task.Delay(50);
        }

        cts.Cancel();
        await runTask;

        Assert.Equal(2, (await repository.QueryAsync(new LogQuery(), CancellationToken.None)).Total);
    }

    private sealed class TestableWorker(
        Channel<LogEntry> channel,
        ILogRepository repository,
        IDbContextFactory<LogDbContext> dbFactory,
        IOptions<LogStoreOptions> options,
        ILogger fallbackLogger,
        LogDropCounter dropCounter) : LogWriteWorker(channel, repository, dbFactory, options, fallbackLogger, dropCounter)
    {
        public Task RunAsync(CancellationToken cancellationToken) => ExecuteAsync(cancellationToken);
    }

    private sealed class ListLogger(List<string> log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            log.Add($"{logLevel}: {formatter(state, exception)} {exception}");
    }

    private sealed class TestContextFactory(DbContextOptions<LogDbContext> options) : IDbContextFactory<LogDbContext>
    {
        public LogDbContext CreateDbContext() => new(options);

        public Task<LogDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
