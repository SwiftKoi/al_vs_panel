using AlegacyWebPanel.Modules.Analytics.Contracts;

namespace AlegacyWebPanel.Modules.Analytics.Services;

public sealed record PlayerSession(
    string PlayerName,
    string RemoteAddress,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    SessionEndKind EndKind,
    string? EndReason,
    bool QuickRejoin)
{
    public bool IsDrop => EndKind is SessionEndKind.LostConnection or SessionEndKind.ClientCrash;
}

// Pairs each join with the player's next session end. A join followed directly
// by another join has an unknown end (the end line is missing, e.g. a server
// crash); the latest join without an end is still open.
public static class SessionBuilder
{
    public static readonly TimeSpan QuickRejoinWindow = TimeSpan.FromMinutes(5);

    public static IReadOnlyList<PlayerSession> Build(
        IEnumerable<PlayerJoin> joins,
        IEnumerable<PlayerSessionEnd> ends)
    {
        var sessions = new List<PlayerSession>();
        var endsByPlayer = ends.ToLookup(end => end.PlayerName, StringComparer.Ordinal);

        foreach (var playerJoins in joins.GroupBy(join => join.PlayerName, StringComparer.Ordinal))
        {
            var ordered = playerJoins.OrderBy(join => join.OccurredAtUtc).ToArray();
            var playerEnds = endsByPlayer[playerJoins.Key].OrderBy(end => end.OccurredAtUtc).ToArray();
            var endIndex = 0;

            for (var i = 0; i < ordered.Length; i++)
            {
                var join = ordered[i];
                var nextJoin = i + 1 < ordered.Length ? ordered[i + 1].OccurredAtUtc : (DateTime?)null;

                while (endIndex < playerEnds.Length && playerEnds[endIndex].OccurredAtUtc < join.OccurredAtUtc)
                {
                    endIndex++;
                }

                PlayerSessionEnd? end = null;
                if (endIndex < playerEnds.Length && (nextJoin is null || playerEnds[endIndex].OccurredAtUtc <= nextJoin))
                {
                    end = playerEnds[endIndex++];
                }

                var kind = end is not null ? Classify(end.Reason)
                    : nextJoin is not null ? SessionEndKind.Unknown
                    : SessionEndKind.Open;
                var endedAt = end?.OccurredAtUtc ?? nextJoin;
                var quickRejoin = endedAt is not null && nextJoin is not null &&
                                  kind != SessionEndKind.ServerShutdown &&
                                  nextJoin.Value - endedAt.Value <= QuickRejoinWindow;

                sessions.Add(new PlayerSession(
                    join.PlayerName, join.RemoteAddress, join.OccurredAtUtc, endedAt, kind, end?.Reason, quickRejoin));
            }
        }

        return sessions;
    }

    // Sessions whose end was never logged are capped so a missing line (e.g. a
    // server crash) does not count as hours of play.
    public static readonly TimeSpan UnknownEndCap = TimeSpan.FromHours(2);
    public static readonly TimeSpan OpenSessionCap = TimeSpan.FromHours(24);

    public static DateTime EffectiveEnd(PlayerSession session, DateTime nowUtc) => session.EndKind switch
    {
        SessionEndKind.Open => Min(nowUtc, session.StartedAtUtc + OpenSessionCap),
        SessionEndKind.Unknown => Min(session.EndedAtUtc ?? nowUtc, session.StartedAtUtc + UnknownEndCap),
        _ => session.EndedAtUtc ?? nowUtc
    };

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    public static SessionEndKind Classify(string? reason)
    {
        if (reason is null)
        {
            return SessionEndKind.Left;
        }

        if (reason.StartsWith("Lost connection", StringComparison.OrdinalIgnoreCase))
        {
            return SessionEndKind.LostConnection;
        }

        if (reason.Contains("Сбой клиента", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("client crash", StringComparison.OrdinalIgnoreCase))
        {
            return SessionEndKind.ClientCrash;
        }

        if (reason.StartsWith("Server shutting down", StringComparison.OrdinalIgnoreCase))
        {
            return SessionEndKind.ServerShutdown;
        }

        if (reason.Contains("exception at the server", StringComparison.OrdinalIgnoreCase))
        {
            return SessionEndKind.ServerError;
        }

        return SessionEndKind.Kicked;
    }
}
