using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlegacyWebPanel.Modules.ServerManagement.Contracts;

[JsonConverter(typeof(ServerRuntimeStatusJsonConverter))]
public enum ServerRuntimeStatus
{
    Online,
    Offline,
    Unknown
}

[JsonConverter(typeof(ServerLifecycleActionJsonConverter))]
public enum ServerLifecycleAction
{
    Start,
    Stop,
    Restart
}

public sealed record ServerSummary(
    string Id,
    string Name,
    string Host,
    int Port,
    string Location);

public sealed record ServerStatusResponse(string ServerId, ServerRuntimeStatus Status);

public sealed record ServerLifecycleResponse(
    string ServerId,
    ServerLifecycleAction Action,
    ServerRuntimeStatus Status);

public sealed record SendServerCommandRequest(string Command);

public sealed record SendServerCommandResponse(string ServerId, bool Accepted);

public sealed record SetGameModeRequest(string PlayerName, int Mode);

/// <summary>Pretty: as in the coordinates box (x y z). Absolute: debug-screen values (=x). Relative: offset from the player (~x).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TeleportCoordinates>))]
public enum TeleportCoordinates
{
    Pretty,
    Absolute,
    Relative
}

public sealed record TeleportRequest(string PlayerName, TeleportCoordinates Coordinates, double X, double Y, double Z);

public sealed record PlayerRequest(string PlayerName);

[JsonConverter(typeof(JsonStringEnumConverter<LandClaimSetting>))]
public enum LandClaimSetting
{
    /// <summary>Extra land claim allowance (<c>landclaimallowance</c>).</summary>
    Allowance,
    /// <summary>Extra land claim areas (<c>landclaimmaxareas</c>).</summary>
    MaxAreas
}

public sealed record LandClaimRequest(string PlayerName, LandClaimSetting Setting, int Value);

public sealed record PlayerReasonRequest(string PlayerName, string? Reason);

public sealed record ServerMetricsResponse(
    string ServerId,
    decimal CpuPercent,
    string MemoryUsage,
    string MemoryLimit,
    decimal MemoryPercent,
    string BlockRead,
    string BlockWrite,
    long DiskUsedBytes,
    long DiskTotalBytes,
    long DiskAvailableBytes,
    decimal DiskPercent,
    DateTimeOffset? StartedAtUtc = null);

public sealed record ServerConnectionsResponse(
    string ServerId,
    IReadOnlyList<ServerClientConnection> Connections);

public sealed record ServerClientConnection(
    string RemoteAddress,
    int RemotePort,
    int LocalPort,
    string? PlayerName,
    int JoinCount,
    decimal RttMs,
    decimal RttVarianceMs,
    decimal MinRttMs,
    decimal RetransmitPercent,
    long RetransmitsTotal,
    int UnackedSegments,
    long ReceiveQueueBytes,
    long SendQueueBytes,
    long BytesSent,
    long BytesReceived,
    long LastReceiveMs,
    long LastSendMs);

public enum ServerLogEventKind
{
    Line,
    Error,
    End
}

public sealed record ServerLogEvent(ServerLogEventKind Kind, string? Data = null);

public sealed class ServerRuntimeStatusJsonConverter()
    : JsonStringEnumConverter<ServerRuntimeStatus>(JsonNamingPolicy.CamelCase);

public sealed class ServerLifecycleActionJsonConverter()
    : JsonStringEnumConverter<ServerLifecycleAction>(JsonNamingPolicy.CamelCase);
