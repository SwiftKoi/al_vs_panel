using AlegacyWebPanel.Modules.ServerManagement.Contracts;

namespace AlegacyWebPanel.Modules.ServerManagement.Services;

public interface IServerManagementService
{
    Task<IReadOnlyList<ServerSummary>> ListAsync(CancellationToken cancellationToken);
    Task<ServerStatusResponse> GetStatusAsync(string serverId, CancellationToken cancellationToken);
    Task<ServerLifecycleResponse> ExecuteLifecycleAsync(
        string serverId,
        ServerLifecycleAction action,
        CancellationToken cancellationToken);
    Task<SendServerCommandResponse> SendCommandAsync(
        string serverId,
        string command,
        CancellationToken cancellationToken);
    Task<ServerMetricsResponse> GetMetricsAsync(string serverId, CancellationToken cancellationToken);
    Task<IAsyncEnumerable<ServerLogEvent>> OpenLogStreamAsync(
        string serverId,
        CancellationToken cancellationToken);
}
