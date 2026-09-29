using AlegacyWebPanel.Modules.ServerLogs.Contracts;
using AlegacyWebPanel.Modules.ServerLogs.Services;
using AlegacyWebPanel.Modules.ServerLogs.Services.Parsing;

namespace AlegacyWebPanel.Modules.ServerLogs.Persistence;

public sealed record TrackedLogFile(long Id, string Identity, string Kind, string Path, long Size, long Offset, long? LastEntryId);

public sealed record LogFilter(string ServerId, LogQuery Query, long FromMs, long ToMs, bool IncludeNoise);

public sealed record LogCursor(long TimestampMs, long Id);

public sealed record SignatureRow(
    long Id, string Kind, string Level, string? Source, string Template, long FirstSeenMs, bool Muted,
    long Count, long LastSeenMs, string SampleMessage);

public sealed record LogIndexStats(long Entries, long IndexedBytes, long TotalBytes, int Files, long? OldestMs, long? NewestMs);

/// <summary>The panel's own index of game-server log entries (SQLite with FTS5).</summary>
public interface ILogIndexRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TrackedLogFile>> GetFilesAsync(string serverId, CancellationToken cancellationToken);
    Task<TrackedLogFile> AddFileAsync(string serverId, string identity, string kind, string path, CancellationToken cancellationToken);
    Task UpdateFileAsync(long fileId, string path, long size, CancellationToken cancellationToken);

    /// <summary>
    /// Stores one parsed chunk and moves the file's offset in one transaction. Entries older than
    /// <paramref name="minimumTimestampMs"/> are skipped (already past retention). Returns the file's new last entry.
    /// </summary>
    Task<long?> AppendAsync(string serverId, TrackedLogFile file, long newOffset, long size, LogChunk chunk,
        long minimumTimestampMs, CancellationToken cancellationToken);

    Task<int> PruneAsync(string serverId, string kind, long beforeMs, CancellationToken cancellationToken);

    Task<IReadOnlyList<LogEntryDto>> SearchAsync(LogFilter filter, LogCursor? cursor, int limit, CancellationToken cancellationToken);
    Task<LogFacetsResponse> FacetsAsync(LogFilter filter, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<long>> HistogramAsync(LogFilter filter, long bucketMs, int buckets, CancellationToken cancellationToken);

    Task<LogEntryDto?> GetEntryAsync(string serverId, long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<LogEntryDto>> ContextAsync(string serverId, LogEntryDto focus, int before, int after, bool includeNoise,
        IReadOnlyCollection<string> logs, CancellationToken cancellationToken);

    Task<IReadOnlyList<SignatureRow>> SignaturesAsync(string serverId, long fromMs, long toMs, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<long, long[]>> SignatureTrendsAsync(string serverId, IReadOnlyCollection<long> ids, long fromMs,
        long bucketMs, int buckets, CancellationToken cancellationToken);
    Task<bool> SetSignatureMutedAsync(string serverId, long id, bool muted, CancellationToken cancellationToken);

    Task<long?> LastServerStartAsync(string serverId, CancellationToken cancellationToken);
    Task<LogIndexStats> StatsAsync(string serverId, CancellationToken cancellationToken);
    long DatabaseBytes();
}
