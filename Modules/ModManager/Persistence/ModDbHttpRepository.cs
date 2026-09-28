using System.Globalization;
using System.Text.Json;
using AlegacyWebPanel.Modules.ModManager.Configuration;
using AlegacyWebPanel.Modules.ModManager.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.ModManager.Persistence;

/// <summary>
/// Official Vintage Story ModDB (<c>GET /api/mod/{modid}</c>). Responses are cached, size-capped,
/// and only accepted from the configured host after redirects.
/// </summary>
public sealed class ModDbHttpRepository(
    HttpClient httpClient,
    IMemoryCache cache,
    IOptions<ModManagerOptions> options,
    ILogger<ModDbHttpRepository> logger) : IModDbRepository
{
    private readonly ModManagerOptions _options = options.Value;

    public async Task<ModDbLookup> GetModAsync(string modId, bool bypassCache, CancellationToken cancellationToken)
    {
        var key = $"moddb:{modId.ToLowerInvariant()}";
        if (!bypassCache && cache.TryGetValue(key, out ModDbLookup? cached) && cached is not null)
        {
            return cached;
        }

        var lookup = await FetchAsync(modId, cancellationToken);
        // Failures are cached briefly so a ModDB outage does not stall every page load.
        var lifetime = lookup.Error is null ? TimeSpan.FromMinutes(_options.CacheMinutes) : TimeSpan.FromMinutes(1);
        cache.Set(key, lookup, lifetime);
        return lookup;
    }

    public async Task DownloadAsync(string url, Stream destination, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(new Uri(_options.ModDbBaseUrl), url, out var uri) || !IsTrusted(uri))
        {
            throw new ModValidationException("The download link does not point to the official ModDB.");
        }

        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || !IsTrusted(finalUri))
        {
            throw new ModValidationException("The download was redirected to an untrusted host.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ModDbUnavailableException($"ModDB returned {(int)response.StatusCode} for the download.");
        }

        if (response.Content.Headers.ContentLength > _options.MaximumDownloadBytes)
        {
            throw new ModValidationException("The mod file is larger than the configured limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await CopyLimitedAsync(source, destination, _options.MaximumDownloadBytes, cancellationToken);
    }

    private async Task<ModDbLookup> FetchAsync(string modId, CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri(new Uri(_options.ModDbBaseUrl), $"/api/mod/{Uri.EscapeDataString(modId)}");
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is null || !finalUri.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return new ModDbLookup(null, "ModDB answered from an unexpected host.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new ModDbLookup(null, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ModDbLookup(null, $"ModDB returned {(int)response.StatusCode}.");
            }

            await using var body = new MemoryStream();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                await CopyLimitedAsync(source, body, _options.MaximumApiResponseBytes, cancellationToken);
            }

            body.Position = 0;
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            var root = document.RootElement;
            // ModDB reports "not found" inside a 200 response as statuscode "404".
            if (ReadString(root, "statuscode") is { } status && status != "200")
            {
                return status == "404"
                    ? new ModDbLookup(null, null)
                    : new ModDbLookup(null, $"ModDB returned status {status}.");
            }

            return root.TryGetProperty("mod", out var mod) && mod.ValueKind == JsonValueKind.Object
                ? new ModDbLookup(ParseMod(modId, mod), null)
                : new ModDbLookup(null, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or ModValidationException
                                          && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "ModDB lookup for {ModId} failed", modId);
            return new ModDbLookup(null, "ModDB could not be reached.");
        }
    }

    private ModDbMod ParseMod(string modId, JsonElement mod)
    {
        var releases = new List<ModDbRelease>();
        if (mod.TryGetProperty("releases", out var releaseArray) && releaseArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var release in releaseArray.EnumerateArray())
            {
                var version = ReadString(release, "modversion");
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                releases.Add(new ModDbRelease(
                    version.Trim(),
                    ReadDate(release, "created"),
                    ReadInt(release, "downloads") ?? 0,
                    ReadString(release, "filename"),
                    ReadString(release, "mainfile"),
                    ReadStrings(release, "tags"),
                    ReadString(release, "changelog")));
            }
        }

        return new ModDbMod(
            modId,
            ReadInt(mod, "assetid") ?? 0,
            ReadString(mod, "name") ?? modId,
            ReadString(mod, "urlalias"),
            ReadString(mod, "side"),
            ReadString(mod, "author"),
            ReadString(mod, "logofiledb") ?? ReadString(mod, "logofile"),
            ReadString(mod, "homepageurl"),
            ReadString(mod, "sourcecodeurl"),
            ReadString(mod, "issuetrackerurl"),
            ReadInt(mod, "downloads"),
            releases);
    }

    private bool IsTrusted(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        _options.TrustedDownloadHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase));

    private static async Task CopyLimitedAsync(Stream source, Stream destination, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > limit)
            {
                throw new ModValidationException("The ModDB response is larger than the configured limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;

    private static int? ReadInt(JsonElement element, string name) =>
        int.TryParse(ReadString(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static DateTimeOffset? ReadDate(JsonElement element, string name) =>
        DateTimeOffset.TryParse(ReadString(element, name), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;

    private static IReadOnlyList<string> ReadStrings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!.Trim()).Where(item => item.Length > 0).ToArray()
            : [];
}
