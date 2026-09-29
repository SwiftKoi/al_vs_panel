using System.Text;
using AlegacyWebPanel.Modules.ServerLogs.Exceptions;

namespace AlegacyWebPanel.Modules.ServerLogs.Services;

/// <summary>
/// A parsed search box. Free words and "quoted phrases" become a full-text query; <c>key:value</c> tokens
/// become filters. Supported keys: log, level, source, player, action, item, near (x,y,z~radius or x,z~radius),
/// sig. Shortcuts that combine an action with what it was done to: command (/name), killed (a creature, or a
/// player's name), killedby (what killed a player), took, put, placed, broke, gave (an item), and with (the other
/// party of any action). Repeating a key ORs its values; different keys AND together. A leading "-" on a basic
/// filter excludes it.
/// </summary>
public sealed class LogQuery
{
    public const int DefaultNearRadius = 32;
    private const int MaximumTerms = 16;

    public List<string> Terms { get; } = [];
    public List<string> Logs { get; } = [];
    public List<string> Levels { get; } = [];
    public List<string> Sources { get; } = [];
    public List<string> Players { get; } = [];
    public List<string> Actions { get; } = [];
    public List<string> Items { get; } = [];
    public List<string> ExcludedLogs { get; } = [];
    public List<string> ExcludedLevels { get; } = [];
    public List<string> ExcludedSources { get; } = [];
    public List<string> ExcludedPlayers { get; } = [];
    public List<string> ExcludedActions { get; } = [];
    /// <summary>Shortcut filters by key (command, killed, killedby, took, put, placed, broke, gave, with).</summary>
    public Dictionary<string, List<string>> Shortcuts { get; } = new(StringComparer.Ordinal);
    public long? SignatureId { get; private set; }

    public static readonly IReadOnlyDictionary<string, string> ShortcutActions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["command"] = "command",
        ["killed"] = "kill",
        ["killedby"] = "death",
        ["took"] = "take",
        ["put"] = "put",
        ["placed"] = "place",
        ["broke"] = "break",
        ["gave"] = "give"
    };
    public (int X, int? Y, int Z, int Radius)? Near { get; private set; }

    public static LogQuery Parse(string? text)
    {
        var query = new LogQuery();
        foreach (var token in Tokenize(text ?? string.Empty))
        {
            query.Add(token);
        }

        if (query.Terms.Count > MaximumTerms)
        {
            throw new LogQueryException($"Use at most {MaximumTerms} search words.");
        }

        return query;
    }

    /// <summary>FTS5 MATCH expression: every term quoted (so no FTS syntax gets through), prefix-matched, ANDed.</summary>
    public string? ToFtsExpression()
    {
        var parts = Terms
            .Select(term => term.Replace("\"", "\"\"", StringComparison.Ordinal).Trim())
            .Where(term => term.Length > 0)
            .Select(term => $"\"{term}\"*")
            .ToArray();
        return parts.Length == 0 ? null : string.Join(' ', parts);
    }

    private void Add(Token token)
    {
        if (token.Key is null)
        {
            if (token.Value.Length > 0)
            {
                Terms.Add(token.Value);
            }

            return;
        }

        var value = token.Value.Trim();
        if (value.Length == 0)
        {
            return;
        }

        switch (token.Key.ToLowerInvariant())
        {
            case "log":
                (token.Negated ? ExcludedLogs : Logs).Add(value.ToLowerInvariant());
                break;
            case "level":
                (token.Negated ? ExcludedLevels : Levels).Add(value);
                break;
            case "source":
                (token.Negated ? ExcludedSources : Sources).Add(value);
                break;
            case "player":
                (token.Negated ? ExcludedPlayers : Players).Add(value);
                break;
            case "action":
                (token.Negated ? ExcludedActions : Actions).Add(value.ToLowerInvariant());
                break;
            case "item":
                Items.Add(value);
                break;
            case "command" or "killed" or "killedby" or "took" or "put" or "placed" or "broke" or "gave" or "with":
                var shortcut = token.Key.ToLowerInvariant();
                if (!Shortcuts.TryGetValue(shortcut, out var values))
                {
                    Shortcuts[shortcut] = values = [];
                }

                values.Add(shortcut == "command" ? "/" + value.TrimStart('/') : value);
                break;
            case "sig":
                SignatureId = long.TryParse(value, out var id) ? id : throw new LogQueryException($"Invalid signature id '{value}'.");
                break;
            case "near":
                Near = ParseNear(value);
                break;
            default:
                // Unknown "key:value" is ordinary text (log lines contain things like "game:firewood").
                Terms.Add(token.Negated ? $"-{token.Key}:{value}" : $"{token.Key}:{value}");
                break;
        }
    }

    private static (int X, int? Y, int Z, int Radius) ParseNear(string value)
    {
        var radius = DefaultNearRadius;
        var parts = value.Split('~');
        if (parts.Length == 2 && (!int.TryParse(parts[1], out radius) || radius is < 1 or > 10_000))
        {
            throw new LogQueryException("near: radius must be between 1 and 10000.");
        }

        var numbers = parts[0].Split(',', StringSplitOptions.TrimEntries);
        var parsed = numbers.Select(n => int.TryParse(n, out var v) ? v : (int?)null).ToArray();
        return parsed switch
        {
            [{ } x, { } y, { } z] => (x, y, z, radius),
            [{ } x, { } z] => (x, null, z, radius),
            _ => throw new LogQueryException("near: expects x,y,z or x,z, optionally followed by ~radius.")
        };
    }

    private readonly record struct Token(string? Key, string Value, bool Negated);

    private static IEnumerable<Token> Tokenize(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (index >= text.Length)
            {
                yield break;
            }

            var negated = false;
            if (text[index] == '-' && index + 1 < text.Length && !char.IsWhiteSpace(text[index + 1]) && !char.IsDigit(text[index + 1]))
            {
                negated = true;
                index++;
            }

            if (text[index] == '"')
            {
                yield return new Token(null, ReadQuoted(text, ref index), false);
                continue;
            }

            var start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] != ':' && text[index] != '"')
            {
                index++;
            }

            var word = text[start..index];
            if (index < text.Length && text[index] == ':' && IsKey(word))
            {
                index++;
                var value = index < text.Length && text[index] == '"' ? ReadQuoted(text, ref index) : ReadWord(text, ref index);
                yield return new Token(word, value, negated);
                continue;
            }

            index = start;
            var rest = ReadWord(text, ref index);
            yield return new Token(null, negated ? $"-{rest}" : rest, false);
        }
    }

    private static bool IsKey(string word) =>
        word.Length is > 0 and <= 12 && word.All(char.IsAsciiLetterLower);

    /// <summary>The query without its free-text words: only its filters (used to narrow autocomplete).</summary>
    public bool HasFilters =>
        Logs.Count + Levels.Count + Sources.Count + Players.Count + Actions.Count + Items.Count + Shortcuts.Count +
        ExcludedLogs.Count + ExcludedLevels.Count + ExcludedSources.Count + ExcludedPlayers.Count + ExcludedActions.Count > 0 ||
        Near is not null || SignatureId is not null;

    private static string ReadWord(string text, ref int index)
    {
        var start = index;
        while (index < text.Length && !char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return text[start..index];
    }

    private static string ReadQuoted(string text, ref int index)
    {
        index++; // opening quote
        var builder = new StringBuilder();
        while (index < text.Length && text[index] != '"')
        {
            builder.Append(text[index++]);
        }

        index = Math.Min(index + 1, text.Length); // closing quote
        return builder.ToString();
    }
}
