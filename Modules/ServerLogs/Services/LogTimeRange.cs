using AlegacyWebPanel.Modules.ServerLogs.Exceptions;

namespace AlegacyWebPanel.Modules.ServerLogs.Services;

internal static class LogTimeRange
{
    public static readonly TimeSpan MaximumRange = TimeSpan.FromDays(400);

    /// <summary>Unix-millisecond bounds; <paramref name="to"/> defaults to now and <paramref name="from"/> to <paramref name="defaultLength"/> before it.</summary>
    public static (long From, long To) Resolve(DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now, TimeSpan defaultLength)
    {
        var end = to ?? now;
        var start = from ?? end - defaultLength;
        if (start >= end)
        {
            throw new LogQueryException("The start of the time range must be before its end.");
        }

        if (end - start > MaximumRange)
        {
            throw new LogQueryException("The time range is too long.");
        }

        return (start.ToUnixTimeMilliseconds(), end.ToUnixTimeMilliseconds());
    }
}
