using AlegacyWebPanel.Modules.ServerManagement.Configuration;
using AlegacyWebPanel.Modules.ServerManagement.Contracts;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.ServerManagement.Persistence;

public sealed class ConfigurationServerRepository(IOptions<ServerManagementOptions> options) : IServerRepository
{
    private readonly IReadOnlyList<ServerDefinition> _servers = options.Value.Instances
        .Select(Map)
        .ToArray();

    public Task<IReadOnlyList<ServerDefinition>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_servers);
    }

    public Task<ServerDefinition?> FindAsync(string serverId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_servers.FirstOrDefault(server =>
            string.Equals(server.Id, serverId, StringComparison.Ordinal)));
    }

    private static ServerDefinition Map(ServerInstanceConfig config) => new(
        config.Id,
        config.Name,
        config.Host,
        config.Port,
        config.Location,
        config.StartOperation,
        config.StopOperation,
        config.RestartOperation,
        config.StatusOperation,
        config.ConsoleOperation,
        config.LogsOperation,
        config.MetricsOperation);
}
