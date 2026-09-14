using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.Logging.Contracts;
using AlegacyWebPanel.Modules.Logging.Endpoints;
using AlegacyWebPanel.Modules.Logging.Exceptions;
using AlegacyWebPanel.Modules.Logging.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

namespace AlegacyWebPanel.Logging.FeatureTests;

public sealed class LoggingEndpointTests
{
    [Fact]
    public async Task Query_maps_query_string_filters_and_returns_page()
    {
        var service = new FakeService();

        var result = await LoggingEndpoints.QueryAsync(
            "warning", "Alegacy", null, null, "disk", 50, 25, service, CancellationToken.None);

        var response = Assert.IsType<Ok<LogPageResult>>(result);
        Assert.Equal(LogLevel.Warning, service.LastQuery?.MinimumLevel);
        Assert.Equal("Alegacy", service.LastQuery?.Source);
        Assert.Equal("disk", service.LastQuery?.Search);
        Assert.Equal(50, service.LastQuery?.Limit);
        Assert.Equal(25, service.LastQuery?.Offset);
        Assert.Equal(1, response.Value!.Items.Count);
    }

    [Fact]
    public async Task Query_applies_default_pagination()
    {
        var service = new FakeService();

        await LoggingEndpoints.QueryAsync(null, null, null, null, null, null, null, service, CancellationToken.None);

        Assert.Equal(100, service.LastQuery?.Limit);
        Assert.Equal(0, service.LastQuery?.Offset);
    }

    [Fact]
    public async Task Query_translates_invalid_level_to_bad_request()
    {
        var service = new FakeService();

        var exception = await Assert.ThrowsAsync<HttpException>(() =>
            LoggingEndpoints.QueryAsync("verbose", null, null, null, null, null, null, service, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task Query_translates_service_rejected_query_to_bad_request()
    {
        var service = new FakeService { Failure = Failure.InvalidQuery };

        var exception = await Assert.ThrowsAsync<HttpException>(() =>
            LoggingEndpoints.QueryAsync(null, null, null, null, null, 5000, null, service, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task Errors_queries_error_and_critical_only()
    {
        var service = new FakeService();

        await LoggingEndpoints.ErrorsAsync(null, null, service, CancellationToken.None);

        Assert.Equal(LogLevel.Error, service.LastQuery?.MinimumLevel);
    }

    [Fact]
    public async Task Sources_returns_distinct_categories()
    {
        var service = new FakeService();

        var result = await LoggingEndpoints.SourcesAsync(service, CancellationToken.None);

        var response = Assert.IsType<Ok<IReadOnlyList<string>>>(result);
        Assert.Equal(["Alegacy.Test"], response.Value);
    }

    [Fact]
    public async Task Summary_returns_level_counts()
    {
        var service = new FakeService();

        var result = await LoggingEndpoints.SummaryAsync(null, null, service, CancellationToken.None);

        var response = Assert.IsType<Ok<LogLevelCounts>>(result);
        Assert.Equal(2, response.Value!.Error);
    }

    [Fact]
    public async Task Delete_returns_deleted_count()
    {
        var service = new FakeService();

        var result = await LoggingEndpoints.DeleteAsync(7, service, CancellationToken.None);

        var response = Assert.IsType<Ok<LogDeleteResult>>(result);
        Assert.Equal(3, response.Value!.Deleted);
    }

    [Fact]
    public async Task Delete_translates_unavailable_store_to_service_unavailable()
    {
        var service = new FakeService { Failure = Failure.Unavailable };

        var exception = await Assert.ThrowsAsync<HttpException>(() =>
            LoggingEndpoints.DeleteAsync(null, service, CancellationToken.None));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
    }

    public enum Failure
    {
        None,
        InvalidQuery,
        Unavailable
    }

    private sealed class FakeService : ILoggingService
    {
        public Failure Failure { get; init; }
        public LogQuery? LastQuery { get; private set; }

        public Task<LogPageResult> QueryAsync(LogQuery query, CancellationToken cancellationToken)
        {
            if (Failure == Failure.InvalidQuery)
            {
                throw new InvalidLogQueryException("invalid");
            }

            if (Failure == Failure.Unavailable)
            {
                throw new LogStoreUnavailableException("unavailable");
            }

            LastQuery = query;
            return Task.FromResult(new LogPageResult(
                [new LogEventDto(1, DateTimeOffset.UtcNow, "Warning", "Alegacy.Test", 0, "disk is full", null, null, null, null)],
                Total: 1,
                query.Offset,
                query.Limit));
        }

        public Task<IReadOnlyList<string>> ListSourcesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["Alegacy.Test"]);

        public Task<LogLevelCounts> GetLevelCountsAsync(
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LogLevelCounts(0, 0, 0, 2, 0));

        public Task<long> DeleteAsync(int? olderThanDays, CancellationToken cancellationToken)
        {
            if (Failure == Failure.Unavailable)
            {
                throw new LogStoreUnavailableException("unavailable");
            }

            return Task.FromResult(3L);
        }
    }
}
