using System.Text.RegularExpressions;
using AlegacyWebPanel.Modules.ServerLogs.Contracts;
using AlegacyWebPanel.Modules.ServerLogs.Persistence;

namespace AlegacyWebPanel.Modules.ServerLogs.Services;

public sealed class BootDraft
{
    public required long StartedMs { get; init; }
    public long? ReadyMs { get; set; }
    public long? StoppedMs { get; set; }
    public string? GameVersion { get; set; }
    public Dictionary<string, string>? Mods { get; set; }
    public string State { get; set; } = "unclean";
}

/// <summary>
/// Turns the main log's startup and shutdown markers into one record per server start:
/// "Server logger started." opens a boot, "Game Version:", "Loaded mods (N):" (with its mod table),
/// "Entering runphase GameReady" and "Stopped the server!" fill it in. A boot followed by another start
/// without "Stopped the server!" in between ended uncleanly (crash, kill, power loss).
/// </summary>
public static partial class BootTimeline
{
    [GeneratedRegex(@"^Game Version: v?(\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex GameVersionPattern();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex ColumnSeparator();

    public static IReadOnlyList<BootDraft> Build(IReadOnlyList<BootMarker> markers)
    {
        var boots = new List<BootDraft>();
        BootDraft? current = null;
        foreach (var marker in markers.OrderBy(marker => marker.TimestampMs))
        {
            if (marker.Message == "Server logger started.")
            {
                current = new BootDraft { StartedMs = marker.TimestampMs };
                boots.Add(current);
                continue;
            }

            if (current is null)
            {
                continue; // markers of a run whose start is no longer in the index
            }

            if (marker.Message == "Entering runphase GameReady")
            {
                current.ReadyMs ??= marker.TimestampMs;
            }
            else if (marker.Message == "Stopped the server!")
            {
                current.StoppedMs ??= marker.TimestampMs;
                current.State = "stopped";
            }
            else if (GameVersionPattern().Match(marker.Message) is { Success: true } version)
            {
                current.GameVersion ??= version.Groups[1].Value;
            }
            else if (marker.Message.StartsWith("Loaded mods (", StringComparison.Ordinal))
            {
                current.Mods ??= ParseModTable(marker.Extra);
            }
        }

        if (boots.Count > 0 && boots[^1].StoppedMs is null)
        {
            boots[^1].State = "running";
        }

        return boots;
    }

    /// <summary>The fixed-width table under "Loaded mods (N):" — first two columns are ModID and Version.</summary>
    public static Dictionary<string, string> ParseModTable(string? table)
    {
        var mods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (table ?? string.Empty).Split('\n'))
        {
            var columns = ColumnSeparator().Split(line.Trim());
            if (columns.Length < 2 || columns[0] is "ModID" or "" || columns[0].StartsWith('-'))
            {
                continue;
            }

            mods[columns[0]] = columns[1];
        }

        return mods;
    }

    /// <summary>Mods added, removed, or at a different version compared with the previous start.</summary>
    public static IReadOnlyList<ModChangeDto> Diff(IReadOnlyDictionary<string, string>? previous, IReadOnlyDictionary<string, string>? current)
    {
        if (previous is null || current is null)
        {
            return [];
        }

        var changes = new List<ModChangeDto>();
        foreach (var (id, version) in current)
        {
            if (!previous.TryGetValue(id, out var old))
            {
                changes.Add(new ModChangeDto(id, "added", null, version));
            }
            else if (!string.Equals(old, version, StringComparison.Ordinal))
            {
                changes.Add(new ModChangeDto(id, "updated", old, version));
            }
        }

        changes.AddRange(previous.Keys.Where(id => !current.ContainsKey(id)).Select(id => new ModChangeDto(id, "removed", previous[id], null)));
        return changes.OrderBy(change => change.Change, StringComparer.Ordinal).ThenBy(change => change.ModId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

}
