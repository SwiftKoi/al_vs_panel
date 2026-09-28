using System.Globalization;
using AlegacyWebPanel.Modules.Analytics.Contracts;

namespace AlegacyWebPanel.Modules.Analytics.Services;

public static class PlayerEventParser
{
    private static readonly string[] TimestampFormats = ["d.M.yyyy H:mm:ss", "dd.MM.yyyy HH:mm:ss"];

    // Parses the typed, tab-separated event lines printed by the player-events
    // operation (see Server/Production/server-player-events.sh). Timestamps are
    // UTC. Malformed lines are skipped so one odd log line cannot block an import.
    public static PlayerEventBatch Parse(string serverId, string output)
    {
        var joins = new List<PlayerJoin>();
        var ends = new List<PlayerSessionEnd>();
        var failures = new List<ConnectionFailure>();
        var pauses = new List<ServerPause>();
        var overloads = new List<ServerOverload>();
        DateTime? suspendedAt = null;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length < 2 || !TryParseTime(fields[1], out var at))
            {
                continue;
            }

            switch (fields[0])
            {
                case "J" when fields.Length == 5 && IsName(fields[2]) && IsAddress(fields[3]) &&
                              int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var port):
                    joins.Add(new PlayerJoin(serverId, at, fields[2], fields[3], port));
                    break;
                case "L" when fields.Length == 3 && IsName(fields[2]):
                    ends.Add(new PlayerSessionEnd(serverId, at, fields[2], null));
                    break;
                case "K" when fields.Length == 4 && IsName(fields[2]):
                    ends.Add(new PlayerSessionEnd(serverId, at, fields[2], Truncate(fields[3])));
                    break;
                case "F" when fields.Length == 4 && IsAddress(fields[2]):
                    failures.Add(new ConnectionFailure(serverId, at, fields[2], Truncate(fields[3])));
                    break;
                case "S" when fields.Length == 2:
                    suspendedAt = at;
                    break;
                case "R" when fields.Length == 2 && suspendedAt is { } started && at >= started:
                    pauses.Add(new ServerPause(serverId, started, at));
                    suspendedAt = null;
                    break;
                case "O" when fields.Length == 3 &&
                              int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var tickMs):
                    overloads.Add(new ServerOverload(serverId, at, tickMs));
                    break;
            }
        }

        return new PlayerEventBatch(joins, ends, failures, pauses, overloads);
    }

    private static bool TryParseTime(string value, out DateTime at) =>
        DateTime.TryParseExact(value, TimestampFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out at);

    private static bool IsName(string value) => value.Length is > 0 and <= 128;

    private static bool IsAddress(string value) => value.Length is > 0 and <= 64;

    private static string Truncate(string value) => value.Length <= 512 ? value : value[..512];
}
