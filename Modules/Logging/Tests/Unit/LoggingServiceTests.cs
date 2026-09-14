using AlegacyWebPanel.Modules.Logging.Contracts;
using AlegacyWebPanel.Modules.Logging.Exceptions;
using AlegacyWebPanel.Modules.Logging.Persistence;
using AlegacyWebPanel.Modules.Logging.Services;
using Microsoft.Extensions.Logging;

namespace AlegacyWebPanel.Logging.UnitTests;

public sealed class LoggingServiceTests
{
    private static LoggingService CreateService(FakeRepository? repository = null) =>
        new(repository ?? new FakeRepository());

    [Fact]
    public async Task Query_maps_repository_entries_to_dtos()
    {
        var repository = new FakeRepository
        {
            QueryResult = new LogQueryResult(
                [new LogEntry(7, DateTimeOffset.UtcNow, "Error", "Alegacy.Test", 0, "failed", null, "System.Exception", "boom", "trace")],
                Total: 1)
        };
        var service = CreateService(repository);

        var result = await service.QueryAsync(new LogQuery(), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal(7, result.Items[0].Id);
        Assert.Equal("failed", result.Items[0].Message);
        Assert.Equal("boom", result.Items[0].ExceptionMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task Query_rejects_out_of_range_page_size(int limit)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidLogQueryException>(() =>
            service.QueryAsync(new LogQuery(Limit: limit), CancellationToken.None));
    }

    [Fact]
    public async Task Query_rejects_negative_offset()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidLogQueryException>(() =>
            service.QueryAsync(new LogQuery(Offset: -1), CancellationToken.None));
    }

    [Fact]
    public async Task Query_rejects_from_after_to()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidLogQueryException>(() =>
            service.QueryAsync(new LogQuery(
                From: DateTimeOffset.UtcNow,
                To: DateTimeOffset.UtcNow.AddMinutes(-1)), CancellationToken.None));
    }

    [Fact]
    public async Task Query_rejects_oversized_filters()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidLogQueryException>(() =>
            service.QueryAsync(new LogQuery(Source: new string('a', 201)), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidLogQueryException>(() =>
            service.QueryAsync(new LogQuery(Search: new string('a', 201)), CancellationToken.None));
    }

    [Fact]
    public async Task Query_forwards_validated_filters_to_repository()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var query = new LogQuery(MinimumLevel: LogLevel.Warning, Source: "Alegacy", Limit: 50, Offset: 25);

        await service.QueryAsync(query, CancellationToken.None);

        Assert.Equal(query, repository.LastQuery);
    }

    [Fact]
    public async Task Counts_rejects_from_after_to()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidLogQueryException>(() =>
            service.GetLevelCountsAsync(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-1), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_rejects_non_positive_retention()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidLogQueryException>(() =>
            service.DeleteAsync(0, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_forwards_valid_retention_filter()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);

        var deleted = await service.DeleteAsync(14, CancellationToken.None);

        Assert.Equal(3, deleted);
        Assert.Equal(14, repository.LastOlderThanDays);
    }

    private sealed class FakeRepository : ILogRepository
    {
        public LogQueryResult QueryResult { get; init; } = new([], 0);
        public LogQuery? LastQuery { get; private set; }
        public int? LastOlderThanDays { get; private set; }

        public Task WriteAsync(IReadOnlyList<LogEntry> entries, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PruneAsync(int maximumRetainedDays, long maximumRetainedEntries, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<LogQueryResult> QueryAsync(LogQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(QueryResult);
        }

        public Task<IReadOnlyList<string>> ListSourcesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<LogLevelCounts> CountByLevelAsync(
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LogLevelCounts(0, 0, 0, 0, 0));

        public Task<long> DeleteAsync(int? olderThanDays, CancellationToken cancellationToken)
        {
            LastOlderThanDays = olderThanDays;
            return Task.FromResult(3L);
        }
    }
}
