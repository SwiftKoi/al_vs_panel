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
    decimal DiskPercent);

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
