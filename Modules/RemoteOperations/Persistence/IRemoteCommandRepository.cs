using AlegacyWebPanel.Modules.RemoteOperations.Contracts;

namespace AlegacyWebPanel.Modules.RemoteOperations.Persistence;

/// <summary>
/// Looks up remote command definitions by operation name.
/// </summary>
public interface IRemoteCommandRepository
{
    /// <summary>
    /// Finds a remote command definition by its operation name.
    /// </summary>
    /// <param name="operation">The operation name to look up.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ///
    /// <returns>The matching <see cref="RemoteCommandDefinition"/>, or null if not found.</returns>
    Task<RemoteCommandDefinition?> FindAsync(string operation, CancellationToken cancellationToken);
}
