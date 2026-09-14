using AlegacyWebPanel.Modules.RemoteOperations.Contracts;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using AlegacyWebPanel.Modules.RemoteOperations.Infrastructure;
using AlegacyWebPanel.Modules.RemoteOperations.Persistence;

namespace AlegacyWebPanel.Modules.RemoteOperations.Services;

public sealed class RemoteOperationsService(
    IRemoteCommandRepository commandRepository,
    IRemoteConnectionFactory connectionFactory,
    ILogger<RemoteOperationsService> logger) : IRemoteOperationsService
{
    public async Task<ExecuteRemoteOperationResponse> ExecuteAsync(
        string operation,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(operation, [], cancellationToken);

    public async Task<ExecuteRemoteOperationResponse> ExecuteAsync(
        string operation,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var definition = await FindDefinitionAsync(operation, cancellationToken);

        logger.LogDebug("Executing remote operation {Operation}", operation);
        var connection = connectionFactory.GetConnection(definition.Target);
        var result = await connection.ExecuteAsync(definition, arguments, cancellationToken);

        if (result.ExitStatus != 0)
        {
            logger.LogWarning(
                "Remote operation {Operation} exited with status {ExitStatus}",
                operation,
                result.ExitStatus);
        }

        return new ExecuteRemoteOperationResponse(result.ExitStatus, result.StandardOutput, result.ErrorOutput);
    }

    public async IAsyncEnumerable<RemoteOperationOutput> StreamAsync(
        string operation,
        IReadOnlyList<string> arguments,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var definition = await FindDefinitionAsync(operation, cancellationToken);
        logger.LogDebug("Streaming remote operation {Operation}", operation);
        var connection = connectionFactory.GetConnection(definition.Target);

        await foreach (var output in connection.StreamAsync(definition, arguments, cancellationToken))
        {
            yield return new RemoteOperationOutput(output.Kind, output.Data, output.ExitStatus);
        }
    }

    public async Task ExecuteBinaryAsync(
        string operation,
        IReadOnlyList<string> arguments,
        Stream? stdin,
        Stream? stdout,
        CancellationToken cancellationToken)
    {
        var definition = await FindDefinitionAsync(operation, cancellationToken);
        logger.LogDebug("Executing binary remote operation {Operation}", operation);
        var connection = connectionFactory.GetConnection(definition.Target);
        await connection.ExecuteBinaryAsync(definition, arguments, stdin, stdout, cancellationToken);
    }

    private async Task<RemoteCommandDefinition> FindDefinitionAsync(
        string operation,
        CancellationToken cancellationToken)
    {
        var definition = await commandRepository.FindAsync(operation, cancellationToken);
        return definition ?? throw new RemoteOperationNotAllowedException(operation);
    }
}
