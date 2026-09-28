using System.Text.Json.Serialization;

namespace AlegacyWebPanel.Modules.Analytics.Contracts;

public sealed record PlayerJoin(
    string ServerId,
    DateTime OccurredAtUtc,
    string PlayerName,
    string RemoteAddress,
    int RemotePort);

public sealed record PlayerSessionEnd(
    string ServerId,
    DateTime OccurredAtUtc,
    string PlayerName,
    string? Reason);

public sealed record ConnectionFailure(
    string ServerId,
    DateTime OccurredAtUtc,
    string RemoteAddress,
    string Reason);

public sealed record ServerPause(string ServerId, DateTime StartedAtUtc, DateTime EndedAtUtc);

public sealed record ServerOverload(string ServerId, DateTime OccurredAtUtc, int TickMs);

public sealed record PlayerEventBatch(
    IReadOnlyList<PlayerJoin> Joins,
    IReadOnlyList<PlayerSessionEnd> SessionEnds,
    IReadOnlyList<ConnectionFailure> Failures,
    IReadOnlyList<ServerPause> Pauses,
    IReadOnlyList<ServerOverload> Overloads);

public sealed record ConnectionSample(
    string ServerId,
    DateTime SampledAtUtc,
    string? PlayerName,
    string RemoteAddress,
    int RemotePort,
    decimal RttMs,
    decimal RttVarianceMs,
    decimal RetransmitPercent,
    long RetransmitsTotal,
    long SendQueueBytes,
    long LastReceiveMs,
    long BytesSent,
    long BytesReceived);

public sealed record SampleRoundCount(DateTime SampledAtUtc, int Connections);

public sealed record PlayerWindowStats(
    string Window,
    int UniquePlayers,
    int ProxyPlayers,
    int NewPlayers,
    int Joins);

public sealed record PlayerDailyStats(
    string Date,
    int UniquePlayers,
    int ProxyPlayers,
    int NewPlayers,
    int Joins,
    int? PeakConcurrent);

public sealed record PlayerSummaryResponse(
    string ServerId,
    string TimeZone,
    bool ProxyConfigured,
    DateTime? RecordedSinceUtc,
    IReadOnlyList<PlayerWindowStats> Windows,
    IReadOnlyList<PlayerDailyStats> Daily);

[JsonConverter(typeof(SessionEndKindJsonConverter))]
public enum SessionEndKind
{
    Left,
    LostConnection,
    ClientCrash,
    ServerShutdown,
    ServerError,
    Kicked,
    Unknown,
    Open
}

public sealed record DisconnectSummary(
    int Sessions,
    int Drops,
    int QuickRejoins,
    int DropsNearAutosave,
    int GroupDrops,
    int DropsAfterOverload,
    decimal MedianSessionMinutes,
    int ProxySessions,
    int ProxyDrops,
    int DirectSessions,
    int DirectDrops,
    int ConnectionFailures);

public sealed record SessionEndCount(SessionEndKind Kind, int Count);

public sealed record FailureReasonCount(string Reason, int Count);

public sealed record DisconnectDailyStats(string Date, int Sessions, int Drops, int QuickRejoins);

public sealed record PlayerDisconnectStats(
    string PlayerName,
    int Sessions,
    int Drops,
    int QuickRejoins,
    decimal AverageSessionMinutes,
    bool UsesProxy,
    DateTime? LastDropUtc);

public sealed record DropEvent(
    DateTime OccurredAtUtc,
    string PlayerName,
    SessionEndKind Kind,
    string? Reason,
    decimal SessionMinutes,
    bool ViaProxy,
    bool NearAutosave,
    int? SlowestTickMs,
    int SimultaneousDrops,
    decimal? RttMs,
    decimal? RetransmitPercent,
    long? LastReceiveMs);

public sealed record DisconnectReportResponse(
    string ServerId,
    string TimeZone,
    int Days,
    DisconnectSummary Summary,
    IReadOnlyList<SessionEndCount> EndReasons,
    IReadOnlyList<FailureReasonCount> FailureReasons,
    IReadOnlyList<DisconnectDailyStats> Daily,
    IReadOnlyList<PlayerDisconnectStats> Players,
    IReadOnlyList<DropEvent> RecentDrops);

public sealed class SessionEndKindJsonConverter()
    : JsonStringEnumConverter<SessionEndKind>(System.Text.Json.JsonNamingPolicy.CamelCase);

public sealed record ConnectionQualityStats(
    int Samples,
    decimal? MedianRttMs,
    decimal? P95RttMs,
    decimal? AverageJitterMs,
    decimal? LossPercent,
    int Stalls);

public sealed record PlayerConnectionQuality(
    string PlayerName,
    bool UsesProxy,
    DateTime LastSeenUtc,
    ConnectionQualityStats Stats);

public sealed record ConnectionQualityResponse(
    string ServerId,
    int Hours,
    bool ProxyConfigured,
    ConnectionQualityStats All,
    ConnectionQualityStats Proxy,
    ConnectionQualityStats Direct,
    IReadOnlyList<PlayerConnectionQuality> Players);

public sealed record PlayerConnectionPoint(
    DateTime SampledAtUtc,
    decimal RttMs,
    decimal JitterMs,
    decimal? LossPercent,
    long LastReceiveMs,
    long SendQueueBytes);

public sealed record PlayerSessionSpan(DateTime StartedAtUtc, DateTime? EndedAtUtc, SessionEndKind EndKind);

public sealed record PlayerConnectionHistoryResponse(
    string ServerId,
    string PlayerName,
    int Hours,
    bool UsesProxy,
    ConnectionQualityStats Stats,
    IReadOnlyList<PlayerConnectionPoint> Points,
    IReadOnlyList<PlayerSessionSpan> Sessions);

public sealed record ServerMetricSample(
    string ServerId,
    DateTime SampledAtUtc,
    decimal CpuPercent,
    decimal MemoryPercent,
    long MemoryBytes);

public sealed record ServerHealthPoint(
    DateTime SampledAtUtc,
    decimal? CpuPercent,
    decimal? MemoryPercent,
    long? MemoryBytes,
    int Players,
    long BytesOutPerSecond,
    long BytesInPerSecond,
    decimal? LongestPauseSeconds,
    int? SlowestTickMs);

public sealed record ServerPauseSpan(DateTime StartedAtUtc, decimal Seconds);

public sealed record ServerHealthSummary(
    decimal? AverageCpuPercent,
    decimal? PeakCpuPercent,
    decimal? PeakMemoryPercent,
    int PeakPlayers,
    int Pauses,
    decimal? LongestPauseSeconds,
    int PausesOverOneSecond,
    long PeakBytesOutPerSecond,
    int Overloads,
    int OverloadsOverTwoSeconds,
    int? SlowestTickMs,
    int? MedianOverloadTickMs);

public sealed record ServerOverloadSpan(DateTime OccurredAtUtc, int TickMs);

public sealed record ServerHealthResponse(
    string ServerId,
    int Hours,
    ServerHealthSummary Summary,
    IReadOnlyList<ServerHealthPoint> Points,
    IReadOnlyList<ServerPauseSpan> LongestPauses,
    IReadOnlyList<ServerOverloadSpan> SlowestTicks);

public sealed record HeatmapCell(int Weekday, int Hour, decimal AveragePlayers, int PeakPlayers);

public sealed record ActivityHeatmapResponse(
    string ServerId,
    string TimeZone,
    int Days,
    IReadOnlyList<HeatmapCell> Cells);

public sealed record PlayerPlaytime(
    string PlayerName,
    int Sessions,
    decimal TotalMinutes,
    decimal AverageSessionMinutes,
    int Drops,
    int QuickRejoins,
    bool UsesProxy,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc);

public sealed record PlayerListResponse(string ServerId, int Days, IReadOnlyList<PlayerPlaytime> Players);

public sealed record PlayerSessionRow(
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    decimal Minutes,
    SessionEndKind EndKind,
    string? EndReason,
    bool ViaProxy,
    bool QuickRejoin);

public sealed record PlayerProfileResponse(
    string ServerId,
    string PlayerName,
    int Days,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    decimal ProxySessionPercent,
    int Sessions,
    decimal TotalMinutes,
    decimal AverageSessionMinutes,
    int Drops,
    int QuickRejoins,
    IReadOnlyList<SessionEndCount> EndReasons,
    ConnectionQualityStats Quality,
    IReadOnlyList<PlayerSessionRow> RecentSessions);
