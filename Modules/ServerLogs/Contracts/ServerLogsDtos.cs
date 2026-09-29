namespace AlegacyWebPanel.Modules.ServerLogs.Contracts;

public sealed record LogEntryDto(
    long Id,
    DateTimeOffset Timestamp,
    string Log,
    string Level,
    string? Source,
    string? Player,
    string? Action,
    string? Item,
    int? X,
    int? Y,
    int? Z,
    string Message,
    string? Extra,
    long? SignatureId,
    string? Other = null);

public sealed record LogSearchResponse(IReadOnlyList<LogEntryDto> Entries, string? NextCursor, IReadOnlyList<string> Terms);

public sealed record FacetValue(string Value, long Count);

public sealed record LogFacetsResponse(
    long Total,
    IReadOnlyList<FacetValue> Logs,
    IReadOnlyList<FacetValue> Levels,
    IReadOnlyList<FacetValue> Sources,
    IReadOnlyList<FacetValue> Players,
    IReadOnlyList<FacetValue> Actions);

public sealed record LogHistogramResponse(DateTimeOffset From, DateTimeOffset To, long BucketMilliseconds, IReadOnlyList<long> Counts);

public sealed record LogContextResponse(long FocusId, IReadOnlyList<LogEntryDto> Entries);

public sealed record LogSignatureDto(
    long Id,
    string Log,
    string Level,
    string? Source,
    string Template,
    long Count,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    bool IsNew,
    bool Muted,
    IReadOnlyList<long> Trend,
    string SampleMessage);

public sealed record LogSignaturesResponse(DateTimeOffset? LastServerStart, IReadOnlyList<LogSignatureDto> Signatures);

public sealed record LogMuteRequest(bool Muted);

public sealed record LogIndexStatusResponse(
    bool Configured,
    long Entries,
    long IndexedBytes,
    long TotalBytes,
    int Files,
    long DatabaseBytes,
    DateTimeOffset? OldestEntry,
    DateTimeOffset? NewestEntry,
    DateTimeOffset? LastIndexedAt,
    string? LastError);

public sealed record PlayerSummaryDto(
    string Name, long Actions, long Commands, long Kills, long Deaths, long RejectedPositions, long Joins, DateTimeOffset LastSeen);

public sealed record PlayersResponse(IReadOnlyList<PlayerSummaryDto> Players);

public sealed record ItemTotalDto(string Item, long Quantity, long Events);

public sealed record PlaceDto(int X, int? Y, int Z, long Count, DateTimeOffset LastSeen);

public sealed record DayActivityDto(DateTimeOffset Day, long Actions, long RejectedPositions);

public sealed record PlayerActivityResponse(
    string Name,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen,
    IReadOnlyList<FacetValue> Actions,
    IReadOnlyList<ItemTotalDto> Taken,
    IReadOnlyList<ItemTotalDto> Put,
    IReadOnlyList<FacetValue> Kills,
    IReadOnlyList<LogEntryDto> Commands,
    IReadOnlyList<LogEntryDto> Deaths,
    IReadOnlyList<LogEntryDto> Sessions,
    IReadOnlyList<PlaceDto> Places,
    IReadOnlyList<DayActivityDto> Days);

public sealed record LocationPlayerDto(string Name, long Count, IReadOnlyList<FacetValue> Actions, DateTimeOffset FirstSeen, DateTimeOffset LastSeen);

public sealed record LocationResponse(
    int X, int? Y, int Z, int Radius, long Total,
    IReadOnlyList<LocationPlayerDto> Players,
    IReadOnlyList<ItemTotalDto> Taken,
    IReadOnlyList<ItemTotalDto> Put);

public sealed record ModChangeDto(string ModId, string Change, string? From, string? To);

public sealed record BootDto(
    DateTimeOffset StartedAt,
    DateTimeOffset? ReadyAt,
    double? StartupSeconds,
    DateTimeOffset? StoppedAt,
    string State,
    string? GameVersion,
    int? ModCount,
    long StartupWarnings,
    long StartupErrors,
    IReadOnlyList<ModChangeDto> ModChanges);

public sealed record BootsResponse(IReadOnlyList<BootDto> Boots);

public sealed record SavedSearchDto(long Id, string Name, string Query, string Range, DateTimeOffset CreatedAt);

public sealed record SaveSearchRequest(string Name, string Query, string Range);

public sealed record SuggestionsResponse(string Key, IReadOnlyList<FacetValue> Values);

public sealed record ProblemSummaryResponse(bool Configured, DateTimeOffset? LastServerStart, long NewErrors, long NewWarnings);
