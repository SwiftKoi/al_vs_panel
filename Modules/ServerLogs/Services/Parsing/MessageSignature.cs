using System.Text.RegularExpressions;

namespace AlegacyWebPanel.Modules.ServerLogs.Services.Parsing;

/// <summary>
/// Groups repeated warnings and errors: numbers, coordinates and identifiers are replaced by
/// placeholders so "At position 1, 2, 3 …" and "At position 4, 5, 6 …" share one signature.
/// </summary>
public static partial class MessageSignature
{
    public const int MaximumLength = 400;

    [GeneratedRegex(@"-?\d+(?:\.\d+)?\s*,\s*-?\d+(?:\.\d+)?\s*,\s*-?\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex PositionPattern();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.CultureInvariant)]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"\b[0-9a-fA-F]{12,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex HexPattern();

    // "… failed for PlayerName: …" — mods often put the player's name before a colon.
    [GeneratedRegex(@"(?<=\bfor )[^\s:]{1,40}(?=:)", RegexOptions.CultureInvariant)]
    private static partial Regex NameBeforeColonPattern();

    [GeneratedRegex(@"\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    public static bool IsProblemLevel(string level) =>
        level.Equals("Warning", StringComparison.OrdinalIgnoreCase) ||
        level.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
        level.Equals("Fatal", StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string message)
    {
        var text = PositionPattern().Replace(message, "<pos>");
        text = GuidPattern().Replace(text, "<id>");
        text = HexPattern().Replace(text, "<id>");
        text = NameBeforeColonPattern().Replace(text, "<name>");
        text = NumberPattern().Replace(text, "N");
        return text.Length <= MaximumLength ? text : text[..MaximumLength];
    }
}
