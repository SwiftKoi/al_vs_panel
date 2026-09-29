using AlegacyWebPanel.Modules.ServerLogs.Contracts;

namespace AlegacyWebPanel.Modules.ServerLogs.Services;

public sealed record LogSearchRequest(string? Query, DateTimeOffset? From, DateTimeOffset? To, bool IncludeNoise);

public interface IServerLogService
{
    Task<LogSearchResponse> SearchAsync(string serverId, LogSearchRequest request, string? cursor, int? limit, CancellationToken cancellationToken);
    Task<LogFacetsResponse> FacetsAsync(string serverId, LogSearchRequest request, CancellationToken cancellationToken);
    Task<LogHistogramResponse> HistogramAsync(string serverId, LogSearchRequest request, int? buckets, CancellationToken cancellationToken);
    Task<LogContextResponse> ContextAsync(string serverId, long entryId, int? before, int? after, bool includeNoise, string? logs, CancellationToken cancellationToken);
    Task<LogSignaturesResponse> SignaturesAsync(string serverId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
    Task SetSignatureMutedAsync(string serverId, long signatureId, bool muted, CancellationToken cancellationToken);
    Task<LogIndexStatusResponse> StatusAsync(string serverId, CancellationToken cancellationToken);

    /// <summary>Writes matching entries (newest first, capped) as plain log lines or CSV.</summary>
    Task ExportAsync(string serverId, LogSearchRequest request, string format, Stream output, CancellationToken cancellationToken);
}
