using AlegacyWebPanel.Modules.Analytics.Configuration;
using AlegacyWebPanel.Modules.Analytics.Contracts;
using AlegacyWebPanel.Modules.Analytics.Exceptions;
using AlegacyWebPanel.Modules.Analytics.Persistence;
using AlegacyWebPanel.Modules.Analytics.Services;
using AlegacyWebPanel.Modules.ServerManagement.Contracts;
using AlegacyWebPanel.Modules.ServerManagement.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Analytics.UnitTests;

public sealed class AnalyticsServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Summary_counts_unique_proxy_and_new_players_per_window_and_day()
    {
        await using var fixture = await Fixture.CreateAsync(["10.77.0.0/16"]);
        await fixture.Repository.AddJoinsAsync(
            [
                Join(Now.AddDays(-40), "Veteran", "203.0.113.1"),
                Join(Now.AddDays(-3), "Veteran", "203.0.113.1"),
                Join(Now.AddHours(-2), "Veteran", "203.0.113.1"),
                Join(Now.AddHours(-3), "Proxied", "10.77.0.1"),
                Join(Now.AddHours(-1), "Proxied", "10.77.0.1"),
                Join(Now.AddDays(-10), "Newbie", "198.51.100.4")
            ],
            CancellationToken.None);

        var summary = await fixture.Service.GetPlayerSummaryAsync("main", 7, CancellationToken.None);

        var day = summary.Windows.Single(window => window.Window == "day");
        Assert.Equal((2, 1, 1, 3), (day.UniquePlayers, day.ProxyPlayers, day.NewPlayers, day.Joins));
        var month = summary.Windows.Single(window => window.Window == "month");
        Assert.Equal((3, 1, 2), (month.UniquePlayers, month.ProxyPlayers, month.NewPlayers));
        Assert.Equal(7, summary.Daily.Count);
        Assert.Equal("2026-09-28", summary.Daily[^1].Date);
        Assert.Equal(2, summary.Daily[^1].UniquePlayers);
        Assert.True(summary.ProxyConfigured);
        Assert.Equal(Now.AddDays(-40), summary.RecordedSinceUtc);
    }

    [Fact]
    public async Task Summary_reports_daily_peak_from_sample_rounds()
    {
        await using var fixture = await Fixture.CreateAsync([]);
        await fixture.Repository.AddSamplesAsync(
            [Sample(Now.AddHours(-2), 1), Sample(Now.AddHours(-1), 1), Sample(Now.AddHours(-1), 2)],
            CancellationToken.None);

        var summary = await fixture.Service.GetPlayerSummaryAsync("main", 2, CancellationToken.None);

        Assert.Equal(2, summary.Daily[^1].PeakConcurrent);
        Assert.Null(summary.Daily[0].PeakConcurrent);
    }

    [Fact]
    public async Task Import_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync([]);
        var joins = new[] { Join(Now, "A", "203.0.113.1") };

        Assert.Equal(1, await fixture.Repository.AddJoinsAsync(joins, CancellationToken.None));
        Assert.Equal(0, await fixture.Repository.AddJoinsAsync(joins, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task Summary_rejects_out_of_range_days(int days)
    {
        await using var fixture = await Fixture.CreateAsync([]);

        await Assert.ThrowsAsync<InvalidAnalyticsRangeException>(() =>
            fixture.Service.GetPlayerSummaryAsync("main", days, CancellationToken.None));
    }

    [Fact]
    public async Task Summary_rejects_unknown_server()
    {
        await using var fixture = await Fixture.CreateAsync([]);

        await Assert.ThrowsAsync<AnalyticsServerNotFoundException>(() =>
            fixture.Service.GetPlayerSummaryAsync("missing", 7, CancellationToken.None));
    }

    [Fact]
    public void Parser_reads_typed_events_and_skips_malformed_ones()
    {
        var batch = PlayerEventParser.Parse("main",
            "J\t28.9.2026 12:26:27\tMaxMeals\t10.77.0.1\t62367\n" +
            "garbage line\n" +
            "J\t31.12.2026 23:59:59\tBob\t203.0.113.5\tnotaport\n" +
            "L\t28.9.2026 12:40:00\tMaxMeals\n" +
            "K\t28.9.2026 12:41:00\tAnn\tLost connection/disconnected\n" +
            "F\t28.9.2026 12:42:00\t203.0.113.7\tInvalid session\n" +
            "S\t28.9.2026 12:43:05\n" +
            "R\t28.9.2026 12:43:07\n" +
            "R\t28.9.2026 12:44:00\n" +
            "O\t28.9.2026 12:45:00\t1856\n" +
            "O\t28.9.2026 12:45:01\tfast\n");

        var join = Assert.Single(batch.Joins);
        Assert.Equal(new DateTime(2026, 9, 28, 12, 26, 27, DateTimeKind.Utc), join.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, join.OccurredAtUtc.Kind);
        Assert.Equal([null, "Lost connection/disconnected"], batch.SessionEnds.Select(end => end.Reason));
        Assert.Equal("Invalid session", Assert.Single(batch.Failures).Reason);
        var pause = Assert.Single(batch.Pauses);
        Assert.Equal(TimeSpan.FromSeconds(2), pause.EndedAtUtc - pause.StartedAtUtc);
        Assert.Equal(1856, Assert.Single(batch.Overloads).TickMs);
    }

    [Fact]
    public void Loss_is_computed_per_interval_of_the_same_connection()
    {
        // 1% of 1000 bytes retransmitted, then 2% of 2000: 30 of 1000 new bytes (3%).
        var intervals = ConnectionQualityCalculator.WithIntervals(
        [
            Quality(Now.AddMinutes(-2), port: 1, rtt: 50, loss: 1, sent: 1000, lastReceive: 100),
            Quality(Now.AddMinutes(-1), port: 1, rtt: 70, loss: 2, sent: 2000, lastReceive: 6000),
            Quality(Now.AddMinutes(-1), port: 2, rtt: 300, loss: 50, sent: 10, lastReceive: 0)
        ]);

        var stats = ConnectionQualityCalculator.Summarize(intervals);

        Assert.Equal(3, stats.Samples);
        Assert.Equal(3m, stats.LossPercent);
        Assert.Equal(70m, stats.MedianRttMs);
        Assert.Equal(300m, stats.P95RttMs);
        Assert.Equal(1, stats.Stalls);
        Assert.Null(intervals[0].LossPercent);
    }

    [Fact]
    public void Chart_points_are_bucketed_to_the_requested_maximum()
    {
        var intervals = ConnectionQualityCalculator.WithIntervals(Enumerable.Range(0, 10)
            .Select(i => Quality(Now.AddMinutes(i), port: 1, rtt: 10 * i, loss: 0, sent: 100 * (i + 1), lastReceive: i)));

        var points = ConnectionQualityCalculator.ToPoints(intervals, 5);

        Assert.Equal(5, points.Count);
        Assert.Equal(5m, points[0].RttMs);
        Assert.Equal(9, points[^1].LastReceiveMs);
    }

    [Fact]
    public async Task Connection_quality_splits_proxy_and_direct_and_history_includes_sessions()
    {
        await using var fixture = await Fixture.CreateAsync(["10.77.0.1"]);
        await fixture.Repository.AddSamplesAsync(
        [
            Quality(Now.AddMinutes(-2), port: 1, rtt: 150, loss: 0, sent: 100, lastReceive: 0, player: "Proxied", address: "10.77.0.1"),
            Quality(Now.AddMinutes(-1), port: 1, rtt: 160, loss: 0, sent: 200, lastReceive: 0, player: "Proxied", address: "10.77.0.1"),
            Quality(Now.AddMinutes(-1), port: 2, rtt: 40, loss: 0, sent: 200, lastReceive: 0, player: "Direct")
        ], CancellationToken.None);
        await fixture.Repository.AddJoinsAsync([Join(Now.AddMinutes(-3), "Proxied", "10.77.0.1")], CancellationToken.None);

        var quality = await fixture.Service.GetConnectionQualityAsync("main", 24, CancellationToken.None);
        var history = await fixture.Service.GetPlayerConnectionHistoryAsync("main", "Proxied", 24, CancellationToken.None);

        Assert.Equal((2, 1), (quality.Proxy.Samples, quality.Direct.Samples));
        Assert.True(quality.Players.Single(player => player.PlayerName == "Proxied").UsesProxy);
        Assert.Equal(2, history.Points.Count);
        Assert.Equal(SessionEndKind.Open, Assert.Single(history.Sessions).EndKind);
        await Assert.ThrowsAsync<AnalyticsPlayerNotFoundException>(() =>
            fixture.Service.GetPlayerConnectionHistoryAsync("main", "Nobody", 24, CancellationToken.None));
    }

    [Theory]
    [InlineData("7.605GiB", 8165806571L)]
    [InlineData("512MiB", 536870912L)]
    [InlineData("1.5GB", 1500000000L)]
    [InlineData("900kB", 900000L)]
    [InlineData("", 0L)]
    [InlineData("n/a", 0L)]
    public void Memory_sizes_are_parsed_from_docker_units(string value, long expected) =>
        Assert.Equal(expected, MemorySizeParser.ToBytes(value));

    [Fact]
    public async Task Server_health_lines_up_metrics_players_throughput_and_pauses()
    {
        await using var fixture = await Fixture.CreateAsync([]);
        var first = Now.AddMinutes(-2);
        var second = Now.AddMinutes(-1);
        await fixture.Repository.AddMetricSampleAsync(new ServerMetricSample("main", first, 40, 30, 1000), CancellationToken.None);
        await fixture.Repository.AddMetricSampleAsync(new ServerMetricSample("main", second, 80, 32, 1100), CancellationToken.None);
        await fixture.Repository.AddSamplesAsync(
        [
            new ConnectionSample("main", first, "A", "203.0.113.1", 1, 50, 5, 0, 0, 0, 0, 0, 0),
            new ConnectionSample("main", second, "A", "203.0.113.1", 1, 50, 5, 0, 0, 0, 0, 6000, 600),
            new ConnectionSample("main", second, "B", "203.0.113.2", 2, 50, 5, 0, 0, 0, 0, 100, 0)
        ], CancellationToken.None);
        await fixture.Repository.AddEventsAsync(new PlayerEventBatch([], [], [],
            [new ServerPause("main", second.AddSeconds(-10), second.AddSeconds(-7))],
            [new ServerOverload("main", second.AddSeconds(-20), 3100), new ServerOverload("main", second.AddSeconds(-5), 700)]), CancellationToken.None);

        var health = await fixture.Service.GetServerHealthAsync("main", 24, CancellationToken.None);

        Assert.Equal(2, health.Points.Count);
        Assert.Equal((1, 2), (health.Points[0].Players, health.Points[1].Players));
        Assert.Equal((100L, 10L), (health.Points[1].BytesOutPerSecond, health.Points[1].BytesInPerSecond));
        Assert.Equal(3m, health.Points[1].LongestPauseSeconds);
        Assert.Equal((60m, 80m, 2, 1), (health.Summary.AverageCpuPercent!.Value, health.Summary.PeakCpuPercent!.Value,
            health.Summary.PeakPlayers, health.Summary.PausesOverOneSecond));
        Assert.Equal(3m, Assert.Single(health.LongestPauses).Seconds);
        Assert.Equal(3100, health.Points[1].SlowestTickMs);
        Assert.Equal((2, 1, 3100), (health.Summary.Overloads, health.Summary.OverloadsOverTwoSeconds, health.Summary.SlowestTickMs!.Value));
        Assert.Equal(3100, health.SlowestTicks[0].TickMs);
    }

    [Fact]
    public async Task Heatmap_counts_distinct_players_per_weekday_hour()
    {
        await using var fixture = await Fixture.CreateAsync([]);
        // Now is Monday 2026-09-28 12:00 UTC; A plays 10:00-11:30 (two hour cells), B 10:15-10:45.
        await fixture.Repository.AddEventsAsync(new PlayerEventBatch(
            [Join(Now.AddHours(-2), "A", "203.0.113.1"), Join(Now.AddMinutes(-105), "B", "203.0.113.2")],
            [
                new PlayerSessionEnd("main", Now.AddMinutes(-30), "A", null),
                new PlayerSessionEnd("main", Now.AddMinutes(-75), "B", null)
            ],
            [], [], []), CancellationToken.None);

        var heatmap = await fixture.Service.GetActivityHeatmapAsync("main", 7, CancellationToken.None);

        Assert.Equal(7 * 24, heatmap.Cells.Count);
        Assert.Equal(2, heatmap.Cells.Single(cell => cell.Weekday == 0 && cell.Hour == 10).PeakPlayers);
        Assert.Equal(1, heatmap.Cells.Single(cell => cell.Weekday == 0 && cell.Hour == 11).PeakPlayers);
        Assert.Equal(0, heatmap.Cells.Single(cell => cell.Weekday == 0 && cell.Hour == 12).PeakPlayers);
    }

    [Fact]
    public async Task Player_list_and_profile_total_playtime_and_cap_unknown_ends()
    {
        await using var fixture = await Fixture.CreateAsync(["10.77.0.1"]);
        await fixture.Repository.AddEventsAsync(new PlayerEventBatch(
            [
                Join(Now.AddHours(-10), "A", "10.77.0.1"),
                Join(Now.AddHours(-5), "A", "203.0.113.1"),
                Join(Now.AddHours(-3), "A", "203.0.113.1")
            ],
            [
                // First session has no end logged before the next join: capped at 2 h.
                new PlayerSessionEnd("main", Now.AddHours(-4), "A", "Lost connection/disconnected"),
                new PlayerSessionEnd("main", Now.AddHours(-2), "A", null)
            ],
            [], [], []), CancellationToken.None);

        var list = await fixture.Service.GetPlayerListAsync("main", 30, CancellationToken.None);
        var profile = await fixture.Service.GetPlayerProfileAsync("main", "A", 30, CancellationToken.None);

        var player = Assert.Single(list.Players);
        Assert.Equal((3, 240m, 1), (player.Sessions, player.TotalMinutes, player.Drops));
        Assert.True(player.UsesProxy);
        Assert.Equal(33m, profile.ProxySessionPercent);
        Assert.Equal(3, profile.RecentSessions.Count);
        Assert.Equal(SessionEndKind.Left, profile.RecentSessions[0].EndKind);
        await Assert.ThrowsAsync<AnalyticsPlayerNotFoundException>(() =>
            fixture.Service.GetPlayerProfileAsync("main", "Nobody", 30, CancellationToken.None));
    }

    [Fact]
    public void Sessions_pair_joins_with_ends_and_flag_quick_rejoins()
    {
        var sessions = SessionBuilder.Build(
            [
                Join(Now.AddMinutes(-60), "A", "203.0.113.1"),
                Join(Now.AddMinutes(-28), "A", "203.0.113.1"),
                Join(Now.AddMinutes(-10), "A", "203.0.113.1"),
                Join(Now.AddMinutes(-5), "A", "203.0.113.1")
            ],
            [
                new PlayerSessionEnd("main", Now.AddMinutes(-30), "A", "Lost connection/disconnected"),
                new PlayerSessionEnd("main", Now.AddMinutes(-20), "A", null)
            ]);

        Assert.Equal(
            [SessionEndKind.LostConnection, SessionEndKind.Left, SessionEndKind.Unknown, SessionEndKind.Open],
            sessions.Select(session => session.EndKind));
        Assert.Equal([true, false, true, false], sessions.Select(session => session.QuickRejoin));
        Assert.True(sessions[0].IsDrop);
    }

    [Theory]
    [InlineData(null, SessionEndKind.Left)]
    [InlineData("Lost connection/disconnected", SessionEndKind.LostConnection)]
    [InlineData("Сбой клиента игрока", SessionEndKind.ClientCrash)]
    [InlineData("Server shutting down - External close event SIGINT received", SessionEndKind.ServerShutdown)]
    [InlineData("Threw an exception at the server", SessionEndKind.ServerError)]
    [InlineData("Вас выгнал Player X", SessionEndKind.Kicked)]
    public void Session_end_reasons_are_classified(string? reason, SessionEndKind expected) =>
        Assert.Equal(expected, SessionBuilder.Classify(reason));

    [Fact]
    public async Task Disconnect_report_correlates_drops_with_autosaves_groups_and_samples()
    {
        await using var fixture = await Fixture.CreateAsync(["10.77.0.1"]);
        var dropAt = Now.AddHours(-1);
        await fixture.Repository.AddEventsAsync(new PlayerEventBatch(
            [
                Join(dropAt.AddMinutes(-30), "Proxied", "10.77.0.1"),
                Join(dropAt.AddMinutes(-20), "Direct", "203.0.113.1"),
                Join(dropAt.AddMinutes(2), "Proxied", "10.77.0.1"),
                Join(dropAt.AddMinutes(-50), "Calm", "198.51.100.2")
            ],
            [
                new PlayerSessionEnd("main", dropAt, "Proxied", "Lost connection/disconnected"),
                new PlayerSessionEnd("main", dropAt.AddSeconds(20), "Direct", "Сбой клиента игрока"),
                new PlayerSessionEnd("main", dropAt.AddMinutes(-10), "Calm", null)
            ],
            [new ConnectionFailure("main", dropAt, "203.0.113.9", "Invalid session")],
            [new ServerPause("main", dropAt.AddSeconds(-5), dropAt.AddSeconds(-4))],
            [new ServerOverload("main", dropAt.AddSeconds(-30), 2400), new ServerOverload("main", dropAt.AddSeconds(-200), 9000)]),
            CancellationToken.None);
        await fixture.Repository.AddSamplesAsync(
            [new ConnectionSample("main", dropAt.AddMinutes(-1), "Proxied", "10.77.0.1", 1, 140, 30, 9.5m, 80, 5000, 7000, 1, 1)],
            CancellationToken.None);

        var report = await fixture.Service.GetDisconnectReportAsync("main", 7, CancellationToken.None);

        Assert.Equal(4, report.Summary.Sessions);
        Assert.Equal(2, report.Summary.Drops);
        Assert.Equal(1, report.Summary.QuickRejoins);
        Assert.Equal((2, 1, 2, 1), (report.Summary.ProxySessions, report.Summary.ProxyDrops,
            report.Summary.DirectSessions, report.Summary.DirectDrops));
        Assert.Equal(1, report.Summary.ConnectionFailures);
        var proxied = report.RecentDrops.Single(drop => drop.PlayerName == "Proxied");
        Assert.True(proxied.NearAutosave);
        Assert.True(proxied.ViaProxy);
        Assert.Equal(1, proxied.SimultaneousDrops);
        Assert.Equal(2400, proxied.SlowestTickMs);
        Assert.Equal(2, report.Summary.DropsAfterOverload);
        Assert.Equal(140m, proxied.RttMs);
        Assert.Equal(7000, proxied.LastReceiveMs);
        Assert.Equal("Proxied", report.Players[0].PlayerName);
        Assert.DoesNotContain(report.Players, player => player.PlayerName == "Calm");
    }

    [Theory]
    [InlineData("10.77.0.1", true)]
    [InlineData("10.78.0.1", false)]
    [InlineData("::ffff:10.77.3.4", true)]
    [InlineData("203.0.113.9", true)]
    [InlineData("not-an-ip", false)]
    public void Proxy_classifier_matches_single_addresses_and_ranges(string address, bool expected)
    {
        var classifier = new ProxyClassifier(Options.Create(new AnalyticsOptions
        {
            ProxyAddresses = ["10.77.0.0/16", "203.0.113.9"]
        }));

        Assert.Equal(expected, classifier.IsProxy(address));
    }

    private static PlayerJoin Join(DateTime at, string name, string address) => new("main", at, name, address, 1000);

    private static ConnectionSample Quality(
        DateTime at, int port, decimal rtt, decimal loss, long sent, long lastReceive,
        string player = "P", string address = "203.0.113.1") =>
        new("main", at, player, address, port, rtt, 5, loss, 0, 0, lastReceive, sent, 0);

    private static ConnectionSample Sample(DateTime at, int port) =>
        new("main", at, "P" + port, "203.0.113.1", port, 50, 5, 0, 0, 0, 0, 0, 0);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, IAnalyticsRepository repository, IAnalyticsService service)
        {
            _connection = connection;
            Repository = repository;
            Service = service;
        }

        public IAnalyticsRepository Repository { get; }
        public IAnalyticsService Service { get; }

        public static async Task<Fixture> CreateAsync(List<string> proxies)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var dbOptions = new DbContextOptionsBuilder<AnalyticsDbContext>().UseSqlite(connection).Options;
            var repository = new SqliteAnalyticsRepository(new TestDbFactory(dbOptions));
            await repository.EnsureCreatedAsync(CancellationToken.None);
            var options = Options.Create(new AnalyticsOptions { ProxyAddresses = proxies });
            var service = new AnalyticsService(
                repository, new FakeServers(), new ProxyClassifier(options), options, new FixedTime(Now));
            return new Fixture(connection, repository, service);
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private sealed class TestDbFactory(DbContextOptions<AnalyticsDbContext> options) : IDbContextFactory<AnalyticsDbContext>
    {
        public AnalyticsDbContext CreateDbContext() => new(options);
    }

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class FakeServers : IServerManagementService
    {
        public Task<IReadOnlyList<ServerSummary>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServerSummary>>([new ServerSummary("main", "Main", "localhost", 1, "Local")]);

        public Task<ServerStatusResponse> GetStatusAsync(string serverId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ServerLifecycleResponse> ExecuteLifecycleAsync(
            string serverId, ServerLifecycleAction action, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SendServerCommandResponse> SendCommandAsync(
            string serverId, string command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ServerMetricsResponse> GetMetricsAsync(string serverId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ServerConnectionsResponse> GetConnectionsAsync(string serverId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IAsyncEnumerable<ServerLogEvent>> OpenLogStreamAsync(string serverId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
