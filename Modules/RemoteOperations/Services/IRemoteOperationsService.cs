using AlegacyWebPanel.Modules.RemoteOperations.Contracts;

namespace AlegacyWebPanel.Modules.RemoteOperations.Services;

/// <summary>
/// Executes remote operations by resolving the command definition and dispatching it to a remote connection.
/// </summary>
public interface IRemoteOperationsService
{
    /// <summary>
    /// Executes a named remote operation.
    /// </summary>
    /// <param name="operation">The name of the operation to execute.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ///
    /// <returns>An <see cref="ExecuteRemoteOperationResponse"/> containing exit status and output.</returns>
    ///
    /// <exception cref="RemoteOperationNotAllowedException">Thrown when the operation is not permitted.</exception>
    /// <exception cref="NotFoundException">Thrown when the operation definition is not found.</exception>
    Task<ExecuteRemoteOperationResponse> ExecuteAsync(
        string operation,
        CancellationToken cancellationToken);

    Task<ExecuteRemoteOperationResponse> ExecuteAsync(
        string operation,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);

    IAsyncEnumerable<RemoteOperationOutput> StreamAsync(
        string operation,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);

    Task ExecuteBinaryAsync(
        string operation,
        IReadOnlyList<string> arguments,
        Stream? stdin,
        Stream? stdout,
        CancellationToken cancellationToken);
}
