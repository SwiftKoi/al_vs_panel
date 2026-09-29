using System.Globalization;
using System.Text;
using AlegacyWebPanel.Modules.ServerLogs.Configuration;
using AlegacyWebPanel.Modules.ServerLogs.Contracts;
using AlegacyWebPanel.Modules.ServerLogs.Exceptions;
using AlegacyWebPanel.Modules.ServerLogs.Persistence;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.ServerLogs.Services;

public sealed class ServerLogService(
    ILogIndexRepository index,
    LogIndexState state,
    IOptions<ServerLogsOptions> options,
    TimeProvider time) : IServerLogService
{
    private const int DefaultPageSize = 200;
    private const int DefaultBuckets = 60;
    private const int FacetLimit = 25;
    private const int TrendBuckets = 24;
    private static readonly TimeSpan DefaultRange = TimeSpan.FromHours(24);

    private readonly ServerLogsOptions _options = options.Value;

    public async Task<LogSearchResponse> SearchAsync(string serverId, LogSearchRequest request, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        var filter = BuildFilter(serverId, request);
        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, _options.MaximumPageSize);
        var entries = await index.SearchAsync(filter, ParseCursor(cursor), pageSize + 1, cancellationToken);
        var page = entries.Take(pageSize).ToArray();
        var next = entries.Count > pageSize ? FormatCursor(page[^1]) : null;
        return new LogSearchResponse(page, next, filter.Query.Terms);
    }

    public Task<LogFacetsResponse> FacetsAsync(string serverId, LogSearchRequest request, CancellationToken cancellationToken) =>
        index.FacetsAsync(BuildFilter(serverId, request), FacetLimit, cancellationToken);

    public async Task<LogHistogramResponse> HistogramAsync(string serverId, LogSearchRequest request, int? buckets, CancellationToken cancellationToken)
    {
        var filter = BuildFilter(serverId, request);
        var count = Math.Clamp(buckets ?? DefaultBuckets, 1, 500);
        var bucketMs = Math.Max(1000, (long)Math.Ceiling((filter.ToMs - filter.FromMs) / (double)count));
        var counts = await index.HistogramAsync(filter, bucketMs, count, cancellationToken);
        return new LogHistogramResponse(
            DateTimeOffset.FromUnixTimeMilliseconds(filter.FromMs), DateTimeOffset.FromUnixTimeMilliseconds(filter.ToMs), bucketMs, counts);
    }

    public async Task<LogContextResponse> ContextAsync(string serverId, long entryId, int? before, int? after, bool includeNoise, string? logs, CancellationToken cancellationToken)
    {
        EnsureConfigured(serverId);
        var focus = await index.GetEntryAsync(serverId, entryId, cancellationToken) ?? throw new LogEntryNotFoundException(entryId);
        var kinds = (logs ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(kind => kind.ToLowerInvariant()).Distinct().ToArray();
        var entries = await index.ContextAsync(
            serverId, focus,
            Math.Clamp(before ?? 50, 0, _options.MaximumContextLines),
            Math.Clamp(after ?? 50, 0, _options.MaximumContextLines),
            includeNoise, kinds, cancellationToken);
        return new LogContextResponse(focus.Id, entries);
    }

    public async Task<LogSignaturesResponse> SignaturesAsync(string serverId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var (fromMs, toMs) = Range(from, to);
        EnsureConfigured(serverId);
        var lastStart = await index.LastServerStartAsync(serverId, cancellationToken);
        var rows = await index.SignaturesAsync(serverId, fromMs, toMs, cancellationToken);
        var bucketMs = Math.Max(1000, (long)Math.Ceiling((toMs - fromMs) / (double)TrendBuckets));
        var trends = await index.SignatureTrendsAsync(serverId, rows.Select(row => row.Id).ToArray(), fromMs, bucketMs, TrendBuckets, cancellationToken);

        var signatures = rows
            .Select(row => new LogSignatureDto(
                row.Id, row.Kind, row.Level, row.Source, row.Template, row.Count,
                DateTimeOffset.FromUnixTimeMilliseconds(row.FirstSeenMs),
                DateTimeOffset.FromUnixTimeMilliseconds(row.LastSeenMs),
                lastStart is { } start && row.FirstSeenMs >= start,
                row.Muted,
                trends.TryGetValue(row.Id, out var trend) ? trend : [],
                row.SampleMessage))
            .OrderBy(signature => signature.Muted)
            .ThenByDescending(signature => signature.IsNew)
            .ThenByDescending(signature => signature.Level.Equals("Error", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(signature => signature.Count)
            .ToArray();

        return new LogSignaturesResponse(lastStart is { } value ? DateTimeOffset.FromUnixTimeMilliseconds(value) : null, signatures);
    }

    public async Task SetSignatureMutedAsync(string serverId, long signatureId, bool muted, CancellationToken cancellationToken)
    {
        EnsureConfigured(serverId);
        if (!await index.SetSignatureMutedAsync(serverId, signatureId, muted, cancellationToken))
        {
            throw new LogEntryNotFoundException(signatureId);
        }
    }

    public async Task<LogIndexStatusResponse> StatusAsync(string serverId, CancellationToken cancellationToken)
    {
        if (!_options.Servers.ContainsKey(serverId))
        {
            return new LogIndexStatusResponse(false, 0, 0, 0, 0, 0, null, null, null, null);
        }

        var stats = await index.StatsAsync(serverId, cancellationToken);
        var last = state.Get(serverId);
        return new LogIndexStatusResponse(
            true, stats.Entries, stats.IndexedBytes, stats.TotalBytes, stats.Files, index.DatabaseBytes(),
            stats.OldestMs is { } oldest ? DateTimeOffset.FromUnixTimeMilliseconds(oldest) : null,
            stats.NewestMs is { } newest ? DateTimeOffset.FromUnixTimeMilliseconds(newest) : null,
            last?.At, last?.Error);
    }

    public async Task ExportAsync(string serverId, LogSearchRequest request, string format, Stream output, CancellationToken cancellationToken)
    {
        var csv = format.Equals("csv", StringComparison.OrdinalIgnoreCase);
        if (!csv && !format.Equals("txt", StringComparison.OrdinalIgnoreCase))
        {
            throw new LogQueryException("Export format must be 'txt' or 'csv'.");
        }

        var filter = BuildFilter(serverId, request);
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
        if (csv)
        {
            await writer.WriteLineAsync("timestamp_utc,log,level,source,player,action,item,x,y,z,message");
        }

        LogCursor? cursor = null;
        var written = 0;
        while (written < _options.MaximumExportRows)
        {
            var batch = await index.SearchAsync(filter, cursor, Math.Min(1000, _options.MaximumExportRows - written), cancellationToken);
            foreach (var entry in batch)
            {
                await writer.WriteLineAsync(csv ? CsvLine(entry) : TextLine(entry));
            }

            written += batch.Count;
            if (batch.Count == 0 || batch.Count < 1000)
            {
                break;
            }

            cursor = new LogCursor(batch[^1].Timestamp.ToUnixTimeMilliseconds(), batch[^1].Id);
        }
    }

    private LogFilter BuildFilter(string serverId, LogSearchRequest request)
    {
        EnsureConfigured(serverId);
        var (fromMs, toMs) = Range(request.From, request.To);
        return new LogFilter(serverId, LogQuery.Parse(request.Query), fromMs, toMs, request.IncludeNoise);
    }

    private (long From, long To) Range(DateTimeOffset? from, DateTimeOffset? to) =>
        LogTimeRange.Resolve(from, to, time.GetUtcNow(), DefaultRange);

    private void EnsureConfigured(string serverId)
    {
        if (!_options.Servers.ContainsKey(serverId))
        {
            throw new ServerLogsNotConfiguredException(serverId);
        }
    }

    private static LogCursor? ParseCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return null;
        }

        var parts = cursor.Split('_');
        return parts.Length == 2 &&
               long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ts) &&
               long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? new LogCursor(ts, id)
            : throw new LogQueryException("Invalid page cursor.");
    }

    private static string FormatCursor(LogEntryDto entry) =>
        string.Create(CultureInfo.InvariantCulture, $"{entry.Timestamp.ToUnixTimeMilliseconds()}_{entry.Id}");

    // Same shape as the game's own lines, so exported text can be read like the original file.
    private static string TextLine(LogEntryDto entry)
    {
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{entry.Timestamp.UtcDateTime:d.M.yyyy HH:mm:ss} [{entry.Level}] {entry.Message}");
        return entry.Extra is null ? line : line + "\n" + entry.Extra;
    }

    private static string CsvLine(LogEntryDto entry) => string.Join(',',
        entry.Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
        Csv(entry.Log), Csv(entry.Level), Csv(entry.Source), Csv(entry.Player), Csv(entry.Action), Csv(entry.Item),
        entry.X?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        entry.Y?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        entry.Z?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        Csv(entry.Extra is null ? entry.Message : entry.Message + "\n" + entry.Extra));

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Leading = + - @ would be run as formulas by spreadsheet apps.
        if (value[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
