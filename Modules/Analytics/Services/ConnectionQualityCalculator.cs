using AlegacyWebPanel.Modules.Analytics.Contracts;

namespace AlegacyWebPanel.Modules.Analytics.Services;

// Turns stored connection samples into quality figures. Retransmission counters
// in samples are cumulative per TCP connection, so loss is computed from the
// difference between consecutive samples of the same connection (address + port):
// retransmitted bytes added / bytes sent added over the interval.
public static class ConnectionQualityCalculator
{
    public const long StallThresholdMs = 5_000;

    public sealed record IntervalSample(ConnectionSample Sample, decimal? RetransmittedBytes, decimal? SentBytes)
    {
        public decimal? LossPercent => SentBytes is > 0 ? Math.Round(RetransmittedBytes!.Value * 100 / SentBytes.Value, 2) : null;
    }

    public static IReadOnlyList<IntervalSample> WithIntervals(IEnumerable<ConnectionSample> samples)
    {
        var result = new List<IntervalSample>();
        foreach (var connection in samples.GroupBy(sample => (sample.RemoteAddress, sample.RemotePort)))
        {
            ConnectionSample? previous = null;
            foreach (var sample in connection.OrderBy(sample => sample.SampledAtUtc))
            {
                decimal? retransmitted = null;
                decimal? sent = null;
                if (previous is not null && sample.BytesSent >= previous.BytesSent)
                {
                    var delta = RetransmittedBytes(sample) - RetransmittedBytes(previous);
                    if (delta >= 0)
                    {
                        retransmitted = delta;
                        sent = sample.BytesSent - previous.BytesSent;
                    }
                }

                result.Add(new IntervalSample(sample, retransmitted, sent));
                previous = sample;
            }
        }

        return result.OrderBy(interval => interval.Sample.SampledAtUtc).ToArray();
    }

    public static ConnectionQualityStats Summarize(IReadOnlyCollection<IntervalSample> intervals)
    {
        if (intervals.Count == 0)
        {
            return new ConnectionQualityStats(0, null, null, null, null, 0);
        }

        var rtts = intervals.Select(interval => interval.Sample.RttMs).Order().ToArray();
        var sent = intervals.Sum(interval => interval.SentBytes ?? 0);
        var retransmitted = intervals.Sum(interval => interval.RetransmittedBytes ?? 0);
        return new ConnectionQualityStats(
            intervals.Count,
            Percentile(rtts, 0.5m),
            Percentile(rtts, 0.95m),
            Math.Round(intervals.Average(interval => interval.Sample.RttVarianceMs), 1),
            sent > 0 ? Math.Round(retransmitted * 100 / sent, 2) : null,
            intervals.Count(interval => interval.Sample.LastReceiveMs >= StallThresholdMs));
    }

    // Averages points into at most maximumPoints time buckets so long ranges stay light.
    public static IReadOnlyList<PlayerConnectionPoint> ToPoints(
        IReadOnlyList<IntervalSample> intervals,
        int maximumPoints)
    {
        if (intervals.Count == 0)
        {
            return [];
        }

        var bucketSize = (int)Math.Ceiling(intervals.Count / (double)maximumPoints);
        return intervals
            .Select((interval, index) => (interval, bucket: index / bucketSize))
            .GroupBy(item => item.bucket, item => item.interval)
            .Select(bucket =>
            {
                var items = bucket.ToArray();
                var sent = items.Sum(item => item.SentBytes ?? 0);
                var retransmitted = items.Sum(item => item.RetransmittedBytes ?? 0);
                return new PlayerConnectionPoint(
                    items[^1].Sample.SampledAtUtc,
                    Math.Round(items.Average(item => item.Sample.RttMs), 1),
                    Math.Round(items.Average(item => item.Sample.RttVarianceMs), 1),
                    items.Any(item => item.SentBytes is not null)
                        ? (sent > 0 ? Math.Round(retransmitted * 100 / sent, 2) : 0)
                        : null,
                    items.Max(item => item.Sample.LastReceiveMs),
                    items.Max(item => item.Sample.SendQueueBytes));
            })
            .ToArray();
    }

    private static decimal RetransmittedBytes(ConnectionSample sample) =>
        sample.RetransmitPercent * sample.BytesSent / 100;

    private static decimal Percentile(IReadOnlyList<decimal> sorted, decimal percentile)
    {
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return Math.Round(sorted[Math.Clamp(index, 0, sorted.Count - 1)], 1);
    }
}
