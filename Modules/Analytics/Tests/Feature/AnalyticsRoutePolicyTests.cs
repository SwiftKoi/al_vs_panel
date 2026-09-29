using AlegacyWebPanel.Core.Authorization;
using AlegacyWebPanel.Modules.Analytics.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using AlegacyWebPanel.Modules.Analytics.Services;

namespace AlegacyWebPanel.Analytics.FeatureTests;

public sealed class AnalyticsRoutePolicyTests
{
    private static readonly string[] ModeratorRoutes =
    [
        "GET /api/analytics/{serverId}/health",
        "GET /api/analytics/{serverId}/disconnects"
    ];

    [Fact]
    public void Only_overview_routes_are_open_to_moderators_and_the_rest_stay_admin_only()
    {
        var builder = WebApplication.CreateSlimBuilder();
        // Only registered so parameter inference treats it as a service; no request is executed.
        builder.Services.AddSingleton<IAnalyticsService>(_ => null!);
        var app = builder.Build();
        app.MapAnalyticsModule();

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
