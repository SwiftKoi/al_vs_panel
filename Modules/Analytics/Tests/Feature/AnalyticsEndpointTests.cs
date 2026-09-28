using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.Analytics.Contracts;
using AlegacyWebPanel.Modules.Analytics.Endpoints;
using AlegacyWebPanel.Modules.Analytics.Exceptions;
using AlegacyWebPanel.Modules.Analytics.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AlegacyWebPanel.Analytics.FeatureTests;

public sealed class AnalyticsEndpointTests
{
    [Fact]
    public async Task Player_summary_defaults_to_thirty_days()
    {
        var service = new FakeService();

        var result = await AnalyticsEndpoints.PlayerSummaryAsync("main", null, service, CancellationToken.None);

        Assert.IsType<Ok<PlayerSummaryResponse>>(result);
        Assert.Equal(30, service.RequestedDays);
    }

    [Fact]
    public async Task Disconnect_report_defaults_to_seven_days()
    {
        var service = new FakeService();

        var result = await AnalyticsEndpoints.DisconnectReportAsync("main", null, service, CancellationToken.None);

        Assert.IsType<Ok<DisconnectReportResponse>>(result);
        Assert.Equal(7, service.RequestedDays);
    }

    [Fact]
    public async Task Connection_quality_defaults_to_twenty_four_hours()
    {
        var service = new FakeService();

        await AnalyticsEndpoints.ConnectionQualityAsync("main", null, service, CancellationToken.None);

        Assert.Equal(24, service.RequestedDays);
    }

    [Fact]
    public async Task Heatmap_defaults_to_four_weeks()
    {
        var service = new FakeService();

        await AnalyticsEndpoints.ActivityHeatmapAsync("main", null, service, CancellationToken.None);

        Assert.Equal(28, service.RequestedDays);
    }

    [Fact]
    public async Task Player_profile_translates_missing_player_to_not_found()
    {
        var service = new FakeService { Failure = new AnalyticsPlayerNotFoundException("x") };

        var exception = await Assert.ThrowsAsync<HttpException>(() =>
            AnalyticsEndpoints.PlayerProfileAsync("main", "x", null, service, CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task Server_health_defaults_to_twenty_four_hours()
    {
        var service = new FakeService();

        var result = await AnalyticsEndpoints.ServerHealthAsync("main", null, service, CancellationToken.None);

        Assert.IsType<Ok<ServerHealthResponse>>(result);
        Assert.Equal(24, service.RequestedDays);
    }

    [Fact]
    public async Task Player_history_translates_missing_player_to_not_found()
    {
        var service = new FakeService { Failure = new AnalyticsPlayerNotFoundException("x") };

        var exception = await Assert.ThrowsAsync<HttpException>(() =>
            AnalyticsEndpoints.PlayerConnectionHistoryAsync("main", "x", null, service, CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
    }

    [Theory]
    [InlineData(true, StatusCodes.Status404NotFound)]
    [InlineData(false, StatusCodes.Status400BadRequest)]
    public async Task Player_summary_translates_domain_failures(bool notFound, int expectedStatus)
    {
        var service = new FakeService
        {
            Failure = notFound ? new AnalyticsServerNotFoundException("x") : new InvalidAnalyticsRangeException("bad")
        };

        var exception = await Assert.ThrowsAsync<HttpException>(() =>
            AnalyticsEndpoints.PlayerSummaryAsync("x", 500, service, CancellationToken.None));

        Assert.Equal(expectedStatus, exception.StatusCode);
    }

    private sealed class FakeService : IAnalyticsService
    {
        public Exception? Failure { get; init; }
        public int RequestedDays { get; private set; }

        public Task<PlayerSummaryResponse> GetPlayerSummaryAsync(string serverId, int days, CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            RequestedDays = days;
            return Task.FromResult(new PlayerSummaryResponse(serverId, "UTC", false, null, [], []));
        }

        public Task<ActivityHeatmapResponse> GetActivityHeatmapAsync(string serverId, int days, CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            RequestedDays = days;
            return Task.FromResult(new ActivityHeatmapResponse(serverId, "UTC", days, []));
        }

        public Task<PlayerListResponse> GetPlayerListAsync(string serverId, int days, CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            RequestedDays = days;
            return Task.FromResult(new PlayerListResponse(serverId, days, []));
        }

        public Task<PlayerProfileResponse> GetPlayerProfileAsync(
            string serverId, string playerName, int days, CancellationToken cancellationToken) =>
            throw Failure ?? new NotSupportedException();

        public Task<ServerHealthResponse> GetServerHealthAsync(string serverId, int hours, CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            RequestedDays = hours;
            return Task.FromResult(new ServerHealthResponse(
                serverId, hours, new ServerHealthSummary(null, null, null, 0, 0, null, 0, 0, 0, 0, null, null), [], [], []));
        }

        public Task<ConnectionQualityResponse> GetConnectionQualityAsync(string serverId, int hours, CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            RequestedDays = hours;
            var empty = new ConnectionQualityStats(0, null, null, null, null, 0);
            return Task.FromResult(new ConnectionQualityResponse(serverId, hours, false, empty, empty, empty, []));
        }

        public Task<PlayerConnectionHistoryResponse> GetPlayerConnectionHistoryAsync(
            string serverId, string playerName, int hours, CancellationToken cancellationToken) =>
            throw Failure ?? new NotSupportedException();

        public Task<DisconnectReportResponse> GetDisconnectReportAsync(string serverId, int days, CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            RequestedDays = days;
            return Task.FromResult(new DisconnectReportResponse(
                serverId, "UTC", days, new DisconnectSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), [], [], [], [], []));
        }
    }
}
