using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace AlegacyWebPanel.Core.Auditing;

/// <summary>What an audited request was about, extracted without any per-route code.</summary>
public sealed record AuditRequestDescription(string? ServerId, string? Target, string? DetailsJson);

/// <summary>
/// Builds the server, target and details of an audit entry from the request: route values, query
/// string and any bound <c>*Request</c> body DTO. Fields that could hold secrets or file contents
/// are removed, long values are cut, and the whole details object is size-bounded.
/// </summary>
public static class AuditRequestDescriber
{
    public const int MaximumValueLength = 200;
    public const int MaximumDetailsLength = 4000;

    private static readonly Regex SensitiveName = new(
        "pass|secret|token|key|code|otp|cookie|content", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveCommand = new(
        @"pass(word|wd)?\b|secret|token|api.?key", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Body properties that name the object of an action, most specific first.
    private static readonly string[] BodyTargetNames = ["playerName", "username", "path", "zipPath", "sourcePath", "name"];
    private static readonly string[] RouteTargetNames = ["modId", "id", "trashId", "signatureId", "taskId", "operation"];

    private static readonly JsonSerializerOptions BodyJson = new(JsonSerializerDefaults.Web);

    public static AuditRequestDescription Describe(HttpContext context, IEnumerable<object?> arguments)
    {
        var details = new JsonObject();
        string? serverId = null;

        foreach (var (key, value) in context.Request.RouteValues)
        {
            var text = value?.ToString();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (string.Equals(key, "serverId", StringComparison.OrdinalIgnoreCase))
            {
                serverId = Truncate(text);
            }
            else
            {
                Add(details, key, text);
            }
        }

        foreach (var (key, values) in context.Request.Query)
        {
            var text = values.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                Add(details, key, text);
            }
        }

        JsonObject? body = null;
        foreach (var argument in arguments)
        {
            if (argument is not null && IsBodyDto(argument.GetType()))
            {
                body = JsonSerializer.SerializeToNode(argument, argument.GetType(), BodyJson) as JsonObject;
                break;
            }
        }

        if (body is not null)
        {
            foreach (var (key, value) in body.ToList())
            {
                if (value is not null && !SensitiveName.IsMatch(key))
                {
                    details[key] = string.Equals(key, "command", StringComparison.OrdinalIgnoreCase)
                                   && value is JsonValue command && command.TryGetValue<string>(out var text)
                        ? RedactCommand(text)
                        : Bound(value);
                }
            }
        }

        var target = PickTarget(details, body);

        var json = details.Count == 0 ? null : details.ToJsonString();
        if (json is { Length: > MaximumDetailsLength })
        {
            json = "{\"truncated\":true}";
        }

        return new AuditRequestDescription(serverId, target, json);
    }

    private static string? PickTarget(JsonObject details, JsonObject? body)
    {
        if (body is not null)
        {
            foreach (var name in BodyTargetNames)
            {
                if (body[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                {
                    return Truncate(text);
                }
            }
        }

        foreach (var name in new[] { "path" }.Concat(RouteTargetNames))
        {
            if (details[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                return Truncate(text);
            }
        }

        return null;
    }

    // A game console command can carry a secret (for example a server password). Keep the command name
    // and drop the arguments when the text mentions anything secret-like.
    private static string RedactCommand(string command)
    {
        if (!SensitiveCommand.IsMatch(command))
        {
            return Truncate(command);
        }

        var name = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return Truncate(name) + " [redacted]";
    }

    private static bool IsBodyDto(Type type) =>
        !type.IsInterface
        && type.Name.EndsWith("Request", StringComparison.Ordinal)
        && type.Namespace?.StartsWith("AlegacyWebPanel", StringComparison.Ordinal) == true;

    private static void Add(JsonObject details, string key, string value)
    {
        if (!SensitiveName.IsMatch(key))
        {
            details[key] = Truncate(value);
        }
    }

    private static JsonNode? Bound(JsonNode value) => value switch
    {
        JsonValue scalar when scalar.TryGetValue<string>(out var text) => Truncate(text),
        JsonObject obj => BoundObject(obj),
        JsonArray array => BoundArray(array),
        _ => value.DeepClone()
    };

    private static JsonObject BoundObject(JsonObject source)
    {
        var result = new JsonObject();
        foreach (var (key, value) in source)
        {
            if (value is not null && !SensitiveName.IsMatch(key))
            {
                result[key] = Bound(value);
            }
        }

        return result;
    }

    private static JsonArray BoundArray(JsonArray source)
    {
        var result = new JsonArray();
        foreach (var item in source.Take(50))
        {
            result.Add(item is null ? null : Bound(item));
        }

        return result;
    }

    private static string Truncate(string value) =>
        value.Length <= MaximumValueLength ? value : value[..MaximumValueLength] + "…";
}
