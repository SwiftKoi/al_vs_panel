using System.Security.Claims;
using AlegacyWebPanel.Core.Auditing;
using AlegacyWebPanel.Core.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AlegacyWebPanel.Core.Tests;

public sealed class AuditEndpointFilterTests
{
    [Fact]
    public async Task A_successful_call_is_recorded_with_actor_role_ip_server_and_target()
    {
        var trail = new RecordingTrail();

        await Invoke(trail, (string serverId) => Results.Ok(), "/api/servers/{serverId}/x", "main", query: "?path=Mods");

        var recorded = Assert.Single(trail.Events);
        Assert.Equal("alice", recorded.Actor);
        Assert.Equal("Admin", recorded.ActorRole);
        Assert.Equal("10.1.2.3", recorded.IpAddress);
        Assert.Equal("files", recorded.Category);
        Assert.Equal("save", recorded.Action);
        Assert.Equal("main", recorded.ServerId);
        Assert.Equal("Mods", recorded.Target);
        Assert.True(recorded.Succeeded);
        Assert.Null(recorded.Error);
    }

    [Fact]
    public async Task A_thrown_error_is_recorded_as_a_failure_and_still_propagates()
    {
        var trail = new RecordingTrail();

        await Assert.ThrowsAsync<HttpException>(() => Invoke(
            trail,
            (string serverId) => ThrowConflict(),
            "/x/{serverId}",
            "main"));

        var recorded = Assert.Single(trail.Events);
        Assert.False(recorded.Succeeded);
        Assert.Equal("Already running", recorded.Error);
    }

    [Fact]
    public async Task An_error_status_result_is_recorded_as_a_failure()
    {
        var trail = new RecordingTrail();

        await Invoke(trail, (string serverId) => Results.StatusCode(StatusCodes.Status403Forbidden), "/x/{serverId}", "main");

        var recorded = Assert.Single(trail.Events);
        Assert.False(recorded.Succeeded);
        Assert.Equal("403", recorded.Error);
    }

    [Fact]
    public async Task A_failing_trail_never_breaks_the_action()
    {
        var trail = new RecordingTrail { Failure = new InvalidOperationException("db down") };

        await Invoke(trail, (string serverId) => Results.NoContent(), "/x/{serverId}", "main");

        Assert.Empty(trail.Events);
    }

    [Fact]
    public async Task Without_a_registered_trail_the_endpoint_works_unchanged()
    {
        await Invoke(trail: null, (string serverId) => Results.NoContent(), "/x/{serverId}", "main");
    }

    [Fact]
    public async Task Calls_without_a_role_claim_are_attributed_to_the_api()
    {
        var trail = new RecordingTrail();

        await Invoke(trail, (string serverId) => Results.Ok(), "/x/{serverId}", "main", role: null, name: "automation-api");

        var recorded = Assert.Single(trail.Events);
        Assert.Equal("automation-api", recorded.Actor);
        Assert.Equal("api", recorded.ActorRole);
    }

    private static async Task Invoke(
        RecordingTrail? trail,
        Delegate handler,
        string pattern,
        string serverId,
        string query = "",
        string? role = "Admin",
        string name = "alice")
    {
        var builder = WebApplication.CreateSlimBuilder();
        if (trail is not null)
        {
            builder.Services.AddSingleton<IAuditTrail>(trail);
        }

        var app = builder.Build();
        app.MapPost(pattern, handler).Audited(AuditCategories.Files, "save");
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Single();

        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        if (role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
        context.Request.QueryString = new QueryString(query);
        context.Request.RouteValues["serverId"] = serverId;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.1.2.3");
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);
    }

    private static IResult ThrowConflict() => throw new HttpException(409, "Conflict", "Already running");

    private sealed class RecordingTrail : IAuditTrail
    {
        public List<AuditEvent> Events { get; } = [];

        public Exception? Failure { get; init; }

        public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
