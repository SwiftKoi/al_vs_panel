using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AlegacyWebPanel.Modules.ModManager.Services;

public sealed record ModInfo(
    string ModId,
    string Name,
    string? Version,
    string? Side,
    string? Description,
    IReadOnlyList<string> Authors);

/// <summary>
/// Reads modinfo.json the way the game does: Newtonsoft with case-insensitive keys, so
/// comments, trailing commas, single quotes and unquoted keys are all accepted.
/// </summary>
public static class ModInfoParser
{
    public static ModInfo? TryParse(byte[] content)
    {
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(content).TrimStart('﻿');
            using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
            if (JToken.ReadFrom(reader, new JsonLoadSettings { CommentHandling = CommentHandling.Ignore }) is not JObject root)
            {
                return null;
            }

            var modId = Read(root, "modid", "modID", "id");
            if (string.IsNullOrWhiteSpace(modId))
            {
                return null;
            }

            return new ModInfo(
                modId.Trim().ToLowerInvariant(),
                Read(root, "name") ?? modId,
                Read(root, "version")?.Trim(),
                Read(root, "side")?.Trim().ToLowerInvariant(),
                Read(root, "description"),
                ReadAuthors(root));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Read(JObject root, params string[] names)
    {
        foreach (var name in names)
        {
            var token = root.GetValue(name, StringComparison.OrdinalIgnoreCase);
            if (token is { Type: JTokenType.String or JTokenType.Integer or JTokenType.Float })
            {
                return token.ToString();
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ReadAuthors(JObject root)
    {
        var token = root.GetValue("authors", StringComparison.OrdinalIgnoreCase)
                    ?? root.GetValue("author", StringComparison.OrdinalIgnoreCase);
        return token switch
        {
            JArray array => array.Where(item => item.Type == JTokenType.String).Select(item => item.ToString()).ToArray(),
            { Type: JTokenType.String } => [token.ToString()],
            _ => []
        };
    }
}
