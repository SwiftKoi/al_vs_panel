using System.Globalization;
using System.Text.RegularExpressions;

namespace AlegacyWebPanel.Modules.ServerLogs.Services.Parsing;

/// <summary>One parsed log entry before it is stored. Continuation lines (stack traces, tables) are joined into <see cref="Extra"/>.</summary>
public sealed class LogEntryDraft
{
    public required long TimestampMs { get; init; }
    public required string Kind { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public string? Source { get; init; }
    public AuditInfo? Audit { get; init; }
    public string? Extra { get; set; }
    public string? Signature { get; init; }
}

public sealed record LogChunk(string? LeadingContinuation, IReadOnlyList<LogEntryDraft> Entries);

/// <summary>
/// Parses the game's log lines: <c>d.M.yyyy HH:mm:ss[.fff] [Level] message</c>. Lines without that header
/// belong to the entry above them. Timestamps are read as UTC, the same assumption the Analytics module makes.
/// </summary>
public static partial class LogLineParser
{
    public const int MaximumExtraLength = 100_000;

    [GeneratedRegex(@"^(\d{1,2})\.(\d{1,2})\.(\d{4}) (\d{1,2}):(\d{2}):(\d{2})(?:\.(\d{1,3}))? \[([A-Za-z]+)\] ?(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderPattern();

    // A leading "[tag] " names the mod or subsystem that wrote a main/debug line.
    [GeneratedRegex(@"^\[([^\]\[]{1,60})\] ", RegexOptions.CultureInvariant)]
    private static partial Regex SourcePattern();

    public static string? KindFromFileName(string fileName) => Path.GetFileName(fileName).ToLowerInvariant() switch
    {
        "server-main.log" => "main",
        "server-audit.log" => "audit",
        "server-debug.log" => "debug",
        "server-chat.log" => "chat",
        _ => null
    };

    public static bool TryParseHeader(string line, out long timestampMs, out string level, out string message)
    {
        timestampMs = 0;
        level = message = string.Empty;
        var match = HeaderPattern().Match(line);
        if (!match.Success)
        {
            return false;
        }

        int Group(int index) => int.Parse(match.Groups[index].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);
        try
        {
            var milliseconds = match.Groups[7].Success ? int.Parse(match.Groups[7].Value.PadRight(3, '0'), CultureInfo.InvariantCulture) : 0;
            var time = new DateTime(Group(3), Group(2), Group(1), Group(4), Group(5), Group(6), milliseconds, DateTimeKind.Utc);
            timestampMs = new DateTimeOffset(time).ToUnixTimeMilliseconds();
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        level = match.Groups[8].Value;
        message = match.Groups[9].Value;
        return true;
    }

    /// <summary>Parses complete lines of one log file. Lines before the first header continue the file's previous entry.</summary>
    public static LogChunk Parse(string kind, string text)
    {
        var entries = new List<LogEntryDraft>();
        // Player kills are logged as "A killed game:player"; the victim is named on A's last damage line just before it.
        var lastHitBy = new Dictionary<string, (string Victim, long Timestamp)>(StringComparer.OrdinalIgnoreCase);
        List<string>? leading = null;
        List<string>? pending = null;
        LogEntryDraft? current = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (TryParseHeader(line, out var timestamp, out var level, out var message))
            {
                Flush();
                current = Create(kind, timestamp, level, message);
                if (current.Audit is { } audit)
                {
                    current = ResolveVictim(current, audit, lastHitBy);
                }

                entries.Add(current);
            }
            else if (line.Length > 0)
            {
                if (current is null)
                {
                    (leading ??= []).Add(line);
                }
                else
                {
                    (pending ??= []).Add(line);
                }
            }
        }

        Flush();
        return new LogChunk(leading is null ? null : Cap(string.Join('\n', leading)), entries);

        void Flush()
        {
            if (current is not null && pending is { Count: > 0 })
            {
                current.Extra = Cap(string.Join('\n', pending));
            }

            pending = null;
        }
    }

    private const long VictimWindowMs = 5_000;

    private static LogEntryDraft ResolveVictim(LogEntryDraft entry, AuditInfo audit, Dictionary<string, (string Victim, long Timestamp)> lastHitBy)
    {
        if (audit is { Action: "damage", Other: { } attacker, Player: { } victim })
        {
            lastHitBy[attacker] = (victim, entry.TimestampMs);
        }
        else if (audit is { Action: "kill", Item: "game:player", Player: { } killer, Other: null } &&
                 lastHitBy.TryGetValue(killer, out var hit) && entry.TimestampMs - hit.Timestamp <= VictimWindowMs)
        {
            return new LogEntryDraft
            {
                TimestampMs = entry.TimestampMs,
                Kind = entry.Kind,
                Level = entry.Level,
                Message = entry.Message,
                Source = entry.Source,
                Audit = audit with { Other = hit.Victim },
                Signature = entry.Signature
            };
        }

        return entry;
    }

    public static string Cap(string text) => text.Length <= MaximumExtraLength ? text : text[..MaximumExtraLength];

    private static LogEntryDraft Create(string kind, long timestamp, string level, string message)
    {
        string? source = null;
        AuditInfo? audit = null;
        if (kind == "audit")
        {
            audit = AuditParser.Parse(message);
        }
        else if (kind is "main" or "debug")
        {
            var match = SourcePattern().Match(message);
            source = match.Success ? match.Groups[1].Value.Trim() : null;
        }

        return new LogEntryDraft
        {
            TimestampMs = timestamp,
            Kind = kind,
            Level = level,
            Message = message,
            Source = source,
            Audit = audit,
            Signature = MessageSignature.IsProblemLevel(level) ? MessageSignature.Normalize(message) : null
        };
    }
}
