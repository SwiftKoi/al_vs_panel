using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.Audit.Contracts;
using AlegacyWebPanel.Modules.Audit.Endpoints;
using AlegacyWebPanel.Modules.Audit.Exceptions;
using AlegacyWebPanel.Modules.Audit.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AlegacyWebPanel.Audit.FeatureTests;

public sealed class AuditEndpointTests
{
    [Fact]
    public async Task Query_maps_filters_trims_blanks_and_applies_default_paging()
    {
        var service = new FakeService();
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        var result = await AuditEndpoints.QueryAsync(
            " alice ", "files", "  ", "main", false, from, null, "world", null, null, service, default);

        Assert.IsType<Ok<AuditPageResult>>(result);
        var query = service.LastQuery!;
        Assert.Equal("alice", query.Actor);
        Assert.Equal("files", query.Category);
        Assert.Null(query.Action);
        Assert.Equal("main", query.ServerId);
        Assert.False(query.Succeeded);
        Assert.Equal(from, query.From);
        Assert.Equal("world", query.Search);
        Assert.Equal(100, query.Limit);
        Assert.Equal(0, query.Offset);
    }

    [Fact]
    public async Task Invalid_queries_become_bad_request()
    {
        var service = new FakeService { Failure = new InvalidAuditQueryException("bad") };

        var exception = await Assert.ThrowsAsync<HttpException>(() =>
            AuditEndpoints.QueryAsync(null, null, null, null, null, null, null, null, null, null, service, default));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task A_broken_store_becomes_service_unavailable()
    {
        var service = new FakeService { Failure = new AuditStoreUnavailableException(new IOException()) };

        var exception = await Assert.ThrowsAsync<HttpException>(() => AuditEndpoints.FacetsAsync(service, default));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public void Every_route_is_read_only_and_admin_only()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<IAuditQueryService>(_ => null!);
        var app = builder.Build();
        app.MapAuditModule();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToList();

        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            // The trail is append-only: it exposes no way to write, edit or delete an entry over HTTP.
            Assert.Equal(["GET"], endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
            // The default policy is admin-only (see PanelPolicies); moderators must not read the audit trail.
            Assert.Equal([null], endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy));
        }
    }

    private sealed class FakeService : IAuditQueryService
    {
        public AuditQuery? LastQuery { get; private set; }

        public Exception? Failure { get; init; }

        public Task<AuditPageResult> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            LastQuery = query;
            return Task.FromResult(new AuditPageResult([], 0, query.Offset, query.Limit));
        }

        public Task<AuditFacets> GetFacetsAsync(CancellationToken cancellationToken) =>
            Failure is not null ? throw Failure : Task.FromResult(new AuditFacets([], [], []));
    }
}
