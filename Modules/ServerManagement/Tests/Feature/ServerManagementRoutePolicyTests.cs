using AlegacyWebPanel.Core.Authorization;
using AlegacyWebPanel.Modules.ServerManagement.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using AlegacyWebPanel.Modules.ServerManagement.Services;

namespace AlegacyWebPanel.ServerManagement.FeatureTests;

public sealed class ServerManagementRoutePolicyTests
{
    private static readonly string[] ModeratorRoutes =
    [
        "GET /api/servers/",
        "GET /api/servers/{serverId}/status",
        "GET /api/servers/{serverId}/metrics",
        "GET /api/servers/{serverId}/connections",
        "POST /api/servers/{serverId}/actions/gamemode",
        "POST /api/servers/{serverId}/actions/teleport",
        "POST /api/servers/{serverId}/actions/warn",
        "POST /api/servers/{serverId}/actions/kick",
        "POST /api/servers/{serverId}/actions/ban",
        "POST /api/servers/{serverId}/actions/unban",
        "POST /api/servers/{serverId}/actions/hardban",
        "POST /api/servers/{serverId}/actions/landclaim",
        "POST /api/servers/{serverId}/actions/allowcharselonce"
    ];

    [Fact]
    public void Only_allowlisted_routes_are_open_to_moderators_and_the_rest_stay_admin_only()
    {
        var builder = WebApplication.CreateSlimBuilder();
        // Only registered so parameter inference treats it as a service; no request is executed.
        builder.Services.AddSingleton<IServerManagementService>(_ => null!);
        builder.Services.AddSingleton<IServerActionsService>(_ => null!);
        var app = builder.Build();
        app.MapServerManagementModule();

        var seen = new List<string>();
        foreach (var endpoint in ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>())
        {
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single();
            var route = $"{method} {endpoint.RoutePattern.RawText}";
            seen.Add(route);
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).ToList();

            // Group and endpoint policies are combined, so a moderator route must carry Staff and nothing else.
            Assert.Equal(ModeratorRoutes.Contains(route) ? [PanelPolicies.Staff] : [null], policies);
        }

        Assert.Subset(seen.ToHashSet(), ModeratorRoutes.ToHashSet());
        Assert.True(seen.Count > ModeratorRoutes.Length);
    }
}
