using System.Collections.Concurrent;
using System.Text;
using AlegacyWebPanel.Modules.ServerLogs.Configuration;
using AlegacyWebPanel.Modules.ServerLogs.Exceptions;
using AlegacyWebPanel.Modules.ServerLogs.Persistence;
using AlegacyWebPanel.Modules.ServerLogs.Services.Parsing;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.ServerLogs.Services;

public sealed record IndexPassResult(long BytesRead, int Entries, bool MoreAvailable);

/// <summary>Last indexing outcome per server, shown on the status endpoint.</summary>
public sealed class LogIndexState
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, string? Error)> _last = new();

    public void Record(string serverId, DateTimeOffset at, string? error) => _last[serverId] = (at, error);

    public (DateTimeOffset At, string? Error)? Get(string serverId) => _last.TryGetValue(serverId, out var value) ? value : null;
}

public interface ILogIndexer
{
    Task<IndexPassResult> IndexAsync(string serverId, ServerLogsServerOptions server, CancellationToken cancellationToken);
    Task PruneAsync(string serverId, CancellationToken cancellationToken);
}

/// <summary>
/// Incremental ingest. Each file is identified by its first line, so when the game moves today's log into
/// <c>Archive/</c> it is recognised and reading continues at the stored offset instead of starting over.
/// Only new bytes are read on each pass; the first pass backfills whatever archives exist, limited per pass.
/// </summary>
public sealed class LogIndexer(
    ILogSourceRepository source,
    ILogIndexRepository index,
    IOptions<ServerLogsOptions> options,
    TimeProvider time,
    ILogger<LogIndexer> logger) : ILogIndexer
{
    private const int DefaultRetentionDays = 30;
    private readonly ServerLogsOptions _options = options.Value;

    public async Task<IndexPassResult> IndexAsync(string serverId, ServerLogsServerOptions server, CancellationToken cancellationToken)
    {
        var files = await source.ListAsync(server.Operation, cancellationToken);
        var tracked = (await index.GetFilesAsync(serverId, cancellationToken)).ToDictionary(file => (file.Kind, file.Identity));
        var budget = _options.MaximumBytesPerPass;
        long bytesRead = 0;
        var entries = 0;
        var more = false;

        // Oldest first: archive folders sort by date, the live files come last.
        foreach (var info in files.OrderBy(file => file.Path.StartsWith("Archive/", StringComparison.Ordinal) ? 0 : 1).ThenBy(file => file.Path, StringComparer.Ordinal))
        {
            var kind = LogLineParser.KindFromFileName(info.Path);
            if (kind is null || info.Identity is null || (kind == "chat" && !server.IncludeChat))
            {
                continue;
            }

            if (!tracked.TryGetValue((kind, info.Identity), out var file))
            {
                file = await index.AddFileAsync(serverId, info.Identity, kind, info.Path, cancellationToken);
            }

            if (info.Size < file.Offset)
            {
                logger.LogWarning("Log file {Path} on {Server} shrank below the indexed offset; skipping it", info.Path, serverId);
                continue;
            }

            if (file.Offset >= info.Size || budget <= 0)
            {
                more |= file.Offset < info.Size;
                if (file.Path != info.Path || file.Size != info.Size)
                {
                    await index.UpdateFileAsync(file.Id, info.Path, info.Size, cancellationToken);
                }

                continue;
            }

            file = file with { Path = info.Path };
            try
            {
                while (file.Offset < info.Size && budget > 0)
                {
                    var bytes = await source.ReadAsync(server.Operation, info.Path, file.Offset, _options.ReadChunkBytes, cancellationToken);
                    if (bytes.Length == 0)
                    {
                        break; // only an incomplete last line so far
                    }

                    var chunk = LogLineParser.Parse(kind, Encoding.UTF8.GetString(bytes));
                    var newOffset = file.Offset + bytes.Length;
                    var lastEntry = await index.AppendAsync(serverId, file, newOffset, info.Size, chunk, RetentionStart(kind), cancellationToken);
                    file = file with { Offset = newOffset, Size = info.Size, LastEntryId = lastEntry };
                    budget -= bytes.Length;
                    bytesRead += bytes.Length;
                    entries += chunk.Entries.Count;
                }

                more |= file.Offset < info.Size && budget <= 0;
            }
            catch (LogSourceException exception)
            {
                // Usually the game rotating the file between "list" and "read"; the next pass finds it in Archive/.
                logger.LogDebug(exception, "Could not read {Path} on {Server}", info.Path, serverId);
            }
        }

        return new IndexPassResult(bytesRead, entries, more);
    }

    public async Task PruneAsync(string serverId, CancellationToken cancellationToken)
    {
        foreach (var kind in new[] { "main", "audit", "debug", "chat" })
        {
            var deleted = await index.PruneAsync(serverId, kind, RetentionStart(kind), cancellationToken);
            if (deleted > 0)
            {
                logger.LogInformation("Pruned {Count} {Kind} log entries of {Server}", deleted, kind, serverId);
            }
        }
    }

    private long RetentionStart(string kind)
    {
        var days = _options.RetentionDays.TryGetValue(kind, out var configured) ? configured : DefaultRetentionDays;
        return time.GetUtcNow().AddDays(-days).ToUnixTimeMilliseconds();
    }
}
