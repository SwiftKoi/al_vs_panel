using System.Globalization;
using System.Text.RegularExpressions;

namespace AlegacyWebPanel.Modules.ServerLogs.Services.Parsing;

/// <summary>
/// Structured fields of one audit line. <see cref="Player"/> has any guild tag ("[RAC] ") and trailing symbols ("♣")
/// removed. <see cref="Other"/> is the other party: the attacker on damage, the killer on deaths, the receiver of a
/// gift, and (filled in by <see cref="LogLineParser"/>) the victim of a player kill.
/// </summary>
public sealed record AuditInfo(string? Player, string Action, string? Item, int? X, int? Y, int? Z, int? Quantity = null, string? Other = null);

/// <summary>
/// Classifies <c>[Audit]</c> messages. Unknown shapes become action "other" so they stay searchable as text.
/// Actions "click" and "packet-rejected" are high-volume noise and hidden from default searches.
/// </summary>
public static partial class AuditParser
{
    public static readonly IReadOnlySet<string> NoiseActions = new HashSet<string>(StringComparer.Ordinal) { "click", "packet-rejected" };

    private const string Actor = @"(?<actor>.+?)";
    private const string Position = @"(?<x>-?\d+), (?<y>-?\d+), (?<z>-?\d+)";

    private static readonly (string Action, Regex Pattern)[] Rules =
    [
        ("click", Rule($@"^{Actor} (?:left |right |middle |shift |)clicked slot ")),
        ("move", Rule($@"^{Actor} moved (?<qty>\d+)x ?(?<item>\S+) from ")),
        ("take", Rule($@"^{Actor} [Tt]ook (?<qty>\d+)x ?(?<item>\S+) from .*? at {Position}")),
        ("put", Rule($@"^{Actor} Put (?<qty>\d+)x ?(?<item>\S+) (?:into|on to|onto) .*? at {Position}")),
        ("give", Rule($@"^{Actor} Gave to (?<other>.+?) (?<qty>\d+)x ?(?<item>\S+) at {Position}")),
        ("packet-rejected", Rule($@"^Player {Actor} sent a packet to .+? at {Position} but is too far away")),
        ("position-rejected", Rule($@"^Rejected (?:player|mount) position update for {Actor}\. Client sent")),
        ("command", Rule($@"^(?:Handling )?command for (?<actor>\S+) (?<item>/\S*)")),
        ("kill", Rule($@"^(?:Player )?{Actor} killed (?<item>\S+) at {Position}")),
        ("death", Rule($@"^{Actor} (?:умер|died)\.(?:.*?(?:убит|killed by) (?<other>.+?)\.?$)?")),
        ("damage", Rule($@"^{Actor} at {Position} got [\d.]+/[\d.]+ damage \S+ (?<item>\S+) by (?<other>.+)$")),
        ("damage", Rule($@"^{Actor} at {Position} got [\d.]+/[\d.]+ damage")),
        ("place", Rule($@"^{Actor} placed an? (?<item>.+?) at {Position}")),
        ("place-rejected", Rule($@"^{Actor} tried to place a block but rejected")),
        ("break", Rule($@"^{Actor} broke (?:container )?(?<item>\S+) at {Position}")),
        ("fire", Rule($@"^{Actor} started a fire at {Position}")),
        ("teleport", Rule($@"^Teleporting player {Actor} from {Position} to ")),
        ("gamemode", Rule($@"^Player {Actor} put (?:himself|herself|themselves) into game mode (?<item>.+)$")),
        ("creative", Rule($@"^{Actor} creative mode created item stack")),
        ("mount", Rule($@"^{Actor} (?:mounts/embarks|dismounts/disembarks) (?:from )?an? (?<item>\S+) at {Position}")),
        ("attach", Rule($@"^{Actor} (?:attached to|removed from) an? .+? at {Position}, slot \d+: .*?Code (?<item>\S+)")),
        ("bag", Rule($@"^{Actor} (?:opened|closed) held bag inventory")),
        ("join", Rule($@"^{Actor} joined\.")),
        ("leave", Rule($@"^Client {Actor} (?:disconnected\.|got removed)"))
    ];

    [GeneratedRegex(@"^\[[^\]]{1,16}\] ", RegexOptions.CultureInvariant)]
    private static partial Regex GuildTagPattern();

    // Mods decorate names with a trailing symbol ("Hikkalibur ♣"); the plain name is the same player.
    [GeneratedRegex(@"\s+[^\p{L}\p{N}_\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingSymbolPattern();

    private static Regex Rule(string pattern) =>
        new(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));

    /// <summary>Normalised player name (no guild tag or trailing symbol), or null when it is not a plausible name.</summary>
    public static string? NormalizePlayer(string? name) =>
        name is null ? null : TrailingSymbolPattern().Replace(GuildTagPattern().Replace(name.Trim(), string.Empty), string.Empty) is { Length: > 0 and <= 80 } plain ? plain : null;

    public static AuditInfo Parse(string message)
    {
        foreach (var (action, pattern) in Rules)
        {
            Match match;
            try
            {
                match = pattern.Match(message);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            if (!match.Success)
            {
                continue;
            }

            return new AuditInfo(
                Player(match.Groups["actor"]),
                action,
                match.Groups["item"] is { Success: true } item ? item.Value.TrimEnd('.', ',') : null,
                Coordinate(match.Groups["x"]),
                Coordinate(match.Groups["y"]),
                Coordinate(match.Groups["z"]),
                Coordinate(match.Groups["qty"]),
                Player(match.Groups["other"]));
        }

        return new AuditInfo(null, "other", null, null, null, null);
    }

    private static string? Player(Group group)
    {
        if (!group.Success)
        {
            return null;
        }

        var name = TrailingSymbolPattern().Replace(GuildTagPattern().Replace(group.Value.Trim(), string.Empty), string.Empty);
        return name.Length is > 0 and <= 80 ? name : null;
    }

    private static int? Coordinate(Group group) =>
        group.Success && int.TryParse(group.ValueSpan, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;
}
