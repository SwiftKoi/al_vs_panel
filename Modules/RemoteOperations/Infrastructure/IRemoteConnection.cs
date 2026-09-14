using AlegacyWebPanel.Modules.RemoteOperations.Contracts;

namespace AlegacyWebPanel.Modules.RemoteOperations.Infrastructure;

/// <summary>
/// Represents a connection to a remote host that can execute shell commands.
/// </summary>
public interface IRemoteConnection
{
    /// <summary>
    /// Executes a command on the remote host.
    /// </summary>
    /// <param name="definition">The command definition specifying the operation name and command string.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ///
    /// <returns>A <see cref="RemoteConnectionResult"/> with exit status, standard output, and error output.</returns>
    ///
    /// <exception cref="RemoteConfigurationException">Thrown when the remote connection is misconfigured.</exception>
    Task<RemoteConnectionResult> ExecuteAsync(
        RemoteCommandDefinition definition,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);

    IAsyncEnumerable<RemoteConnectionOutput> StreamAsync(
        RemoteCommandDefinition definition,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);

    Task ExecuteBinaryAsync(
        RemoteCommandDefinition definition,
        IReadOnlyList<string> arguments,
        Stream? stdin,
        Stream? stdout,
        CancellationToken cancellationToken);
}

public sealed record RemoteConnectionResult(int ExitStatus, string StandardOutput, string ErrorOutput);

public sealed record RemoteConnectionOutput(
    RemoteOperationOutputKind Kind,
    string? Data = null,
    int? ExitStatus = null);
