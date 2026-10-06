using AlegacyWebPanel.Core.Auditing;
using AlegacyWebPanel.Modules.Audit.Configuration;
using AlegacyWebPanel.Modules.Audit.Contracts;
using AlegacyWebPanel.Modules.Audit.Exceptions;
using AlegacyWebPanel.Modules.Audit.Persistence;
using AlegacyWebPanel.Modules.Audit.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Audit.UnitTests;

public sealed class AuditServicesTests
{
    private static readonly AuditEvent Sample = new("alice", "Admin", null, "files", "delete", null, "x", null, true, null);

    [Fact]
    public async Task Trail_stamps_events_with_the_current_time()
    {
        var repository = new FakeRepository();
        var clock = new FixedClock(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var trail = new AuditTrailService(repository, clock, NullLogger<AuditTrailService>.Instance);

        await trail.RecordAsync(Sample);

        Assert.Equal(clock.GetUtcNow(), Assert.Single(repository.Added).Timestamp);
    }

    [Fact]
    public async Task Trail_swallows_storage_failures_so_the_audited_action_is_not_broken()
    {
        var repository = new FakeRepository { Failure = new AuditStoreUnavailableException(new IOException("disk full")) };
        var trail = new AuditTrailService(repository, TimeProvider.System, NullLogger<AuditTrailService>.Instance);

        await trail.RecordAsync(Sample);

        Assert.Empty(repository.Added);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(501, 0)]
    [InlineData(10, -1)]
    public async Task Query_rejects_bad_paging(int limit, int offset)
    {
        var service = CreateQueryService();

        await Assert.ThrowsAsync<InvalidAuditQueryException>(() =>
            service.QueryAsync(new AuditQuery(Limit: limit, Offset: offset), default));
    }

    [Fact]
    public async Task Query_rejects_a_reversed_time_range_and_oversized_filters()
    {
        var service = CreateQueryService();
        var now = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<InvalidAuditQueryException>(() =>
            service.QueryAsync(new AuditQuery(From: now, To: now.AddDays(-1)), default));
        await Assert.ThrowsAsync<InvalidAuditQueryException>(() =>
            service.QueryAsync(new AuditQuery(Search: new string('x', 201)), default));
    }

    [Fact]
    public async Task Query_returns_the_page_with_its_paging_values()
    {
        var repository = new FakeRepository();
        var service = CreateQueryService(repository);

        var page = await service.QueryAsync(new AuditQuery(Limit: 20, Offset: 40), default);

        Assert.Equal(20, page.Limit);
        Assert.Equal(40, page.Offset);
        Assert.Equal(7, page.Total);
    }

    private static AuditQueryService CreateQueryService(FakeRepository? repository = null) =>
        new(repository ?? new FakeRepository(), Options.Create(new AuditOptions { MaximumPageSize = 500 }));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeRepository : IAuditRepository
    {
        public List<(AuditEvent Event, DateTimeOffset Timestamp)> Added { get; } = [];

        public Exception? Failure { get; init; }

        public Task AddAsync(AuditEvent auditEvent, DateTimeOffset timestamp, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Added.Add((auditEvent, timestamp));
            return Task.CompletedTask;
        }

        public Task<AuditQueryResult> QueryAsync(AuditQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new AuditQueryResult([], 7));

        public Task<AuditFacets> GetFacetsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AuditFacets([], [], []));

        public Task<long> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) => Task.FromResult(0L);
    }
}
