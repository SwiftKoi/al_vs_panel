using AlegacyWebPanel.Modules.Analytics.Contracts;

namespace AlegacyWebPanel.Modules.Analytics.Services;

// Combines per-minute server metrics, connection samples, and autosave pauses
// into one time series. Recorder rounds share a timestamp, so metrics and
// connections of the same minute line up exactly.
public static class ServerHealthCalculator
{
    private static readonly TimeSpan MaximumThroughputGap = TimeSpan.FromMinutes(5);

    public static IReadOnlyList<ServerHealthPoint> Build(
        IReadOnlyList<ServerMetricSample> metrics,
        IReadOnlyList<ConnectionSample> connections,
        IReadOnlyList<ServerPause> pauses,
        IReadOnlyList<ServerOverload> overloads,
        int maximumPoints)
    {
        var metricsByTime = metrics
            .GroupBy(metric => metric.SampledAtUtc)
            .ToDictionary(group => group.Key, group => group.First());
        var connectionsByTime = connections.ToLookup(sample => sample.SampledAtUtc);
        var throughput = Throughput(connections);
        var times = metricsByTime.Keys.Concat(connections.Select(sample => sample.SampledAtUtc))
            .Distinct()
            .Order()
            .ToArray();
        var orderedPauses = pauses.OrderBy(pause => pause.StartedAtUtc).ToArray();
        var orderedOverloads = overloads.OrderBy(overload => overload.OccurredAtUtc).ToArray();
        var overloadIndex = 0;

        var rounds = new List<ServerHealthPoint>(times.Length);
        var pauseIndex = 0;
        for (var i = 0; i < times.Length; i++)
        {
            var at = times[i];
            decimal? longestPause = null;
            while (pauseIndex < orderedPauses.Length && orderedPauses[pauseIndex].StartedAtUtc <= at)
            {
                var seconds = (decimal)(orderedPauses[pauseIndex].EndedAtUtc - orderedPauses[pauseIndex].StartedAtUtc).TotalSeconds;
                longestPause = Math.Max(longestPause ?? 0, seconds);
                pauseIndex++;
            }

            int? slowestTick = null;
            while (overloadIndex < orderedOverloads.Length && orderedOverloads[overloadIndex].OccurredAtUtc <= at)
            {
                slowestTick = Math.Max(slowestTick ?? 0, orderedOverloads[overloadIndex].TickMs);
                overloadIndex++;
            }

            metricsByTime.TryGetValue(at, out var metric);
            throughput.TryGetValue(at, out var rate);
            rounds.Add(new ServerHealthPoint(
                at,
                metric?.CpuPercent,
                metric?.MemoryPercent,
                metric?.MemoryBytes,
                connectionsByTime[at].Count(),
                rate.Out,
                rate.In,
                longestPause,
                slowestTick));
        }

        return Bucket(rounds, maximumPoints);
    }

    public static ServerHealthSummary Summarize(
        IReadOnlyList<ServerMetricSample> metrics,
        IReadOnlyList<ServerHealthPoint> points,
        IReadOnlyList<ServerPause> pauses,
        IReadOnlyList<ServerOverload> overloads)
    {
        var durations = pauses.Select(Seconds).ToArray();
        var ticks = overloads.Select(overload => overload.TickMs).Order().ToArray();
        return new ServerHealthSummary(
            metrics.Count == 0 ? null : Math.Round(metrics.Average(metric => metric.CpuPercent), 1),
            metrics.Count == 0 ? null : Math.Round(metrics.Max(metric => metric.CpuPercent), 1),
            metrics.Count == 0 ? null : Math.Round(metrics.Max(metric => metric.MemoryPercent), 1),
            points.Count == 0 ? 0 : points.Max(point => point.Players),
            durations.Length,
            durations.Length == 0 ? null : durations.Max(),
            durations.Count(seconds => seconds > 1),
            points.Count == 0 ? 0 : points.Max(point => point.BytesOutPerSecond),
            ticks.Length,
            ticks.Count(ms => ms > 2000),
            ticks.Length == 0 ? null : ticks[^1],
            ticks.Length == 0 ? null : ticks[ticks.Length / 2]);
    }

    public static decimal Seconds(ServerPause pause) =>
        (decimal)(pause.EndedAtUtc - pause.StartedAtUtc).TotalSeconds;

    private static Dictionary<DateTime, (long Out, long In)> Throughput(IReadOnlyList<ConnectionSample> connections)
    {
        var totals = new Dictionary<DateTime, (long Out, long In)>();
        foreach (var connection in connections.GroupBy(sample => (sample.RemoteAddress, sample.RemotePort)))
        {
            ConnectionSample? previous = null;
            foreach (var sample in connection.OrderBy(sample => sample.SampledAtUtc))
            {
                if (previous is not null)
                {
                    var seconds = (sample.SampledAtUtc - previous.SampledAtUtc).TotalSeconds;
                    var sent = sample.BytesSent - previous.BytesSent;
                    var received = sample.BytesReceived - previous.BytesReceived;
                    if (seconds > 0 && seconds <= MaximumThroughputGap.TotalSeconds && sent >= 0 && received >= 0)
                    {
                        totals.TryGetValue(sample.SampledAtUtc, out var total);
                        totals[sample.SampledAtUtc] = (
                            total.Out + (long)(sent / seconds),
                            total.In + (long)(received / seconds));
                    }
                }

                previous = sample;
            }
        }

        return totals;
    }

    private static IReadOnlyList<ServerHealthPoint> Bucket(IReadOnlyList<ServerHealthPoint> rounds, int maximumPoints)
    {
        if (rounds.Count <= maximumPoints)
        {
            return rounds;
        }

        var size = (int)Math.Ceiling(rounds.Count / (double)maximumPoints);
        return rounds
            .Select((point, index) => (point, bucket: index / size))
            .GroupBy(item => item.bucket, item => item.point)
            .Select(group =>
            {
                var items = group.ToArray();
                var withMetrics = items.Where(item => item.CpuPercent is not null).ToArray();
                var pausesInBucket = items.Where(item => item.LongestPauseSeconds is not null).ToArray();
                var ticksInBucket = items.Where(item => item.SlowestTickMs is not null).ToArray();
                return new ServerHealthPoint(
                    items[^1].SampledAtUtc,
                    withMetrics.Length == 0 ? null : Math.Round(withMetrics.Average(item => item.CpuPercent!.Value), 1),
                    withMetrics.Length == 0 ? null : Math.Round(withMetrics.Average(item => item.MemoryPercent!.Value), 1),
                    withMetrics.Length == 0 ? null : (long)withMetrics.Average(item => item.MemoryBytes!.Value),
                    items.Max(item => item.Players),
                    (long)items.Average(item => item.BytesOutPerSecond),
                    (long)items.Average(item => item.BytesInPerSecond),
                    pausesInBucket.Length == 0 ? null : pausesInBucket.Max(item => item.LongestPauseSeconds),
                    ticksInBucket.Length == 0 ? null : ticksInBucket.Max(item => item.SlowestTickMs));
            })
            .ToArray();
    }
}
