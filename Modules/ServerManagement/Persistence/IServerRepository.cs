using AlegacyWebPanel.Modules.ServerManagement.Contracts;

namespace AlegacyWebPanel.Modules.ServerManagement.Persistence;

public interface IServerRepository
{
    Task<IReadOnlyList<ServerDefinition>> ListAsync(CancellationToken cancellationToken);
    Task<ServerDefinition?> FindAsync(string serverId, CancellationToken cancellationToken);
}
