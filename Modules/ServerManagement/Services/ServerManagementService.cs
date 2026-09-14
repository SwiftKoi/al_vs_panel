using System.Runtime.CompilerServices;
using System.Text.Json;
using AlegacyWebPanel.Modules.RemoteOperations.Contracts;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using AlegacyWebPanel.Modules.RemoteOperations.Services;
using AlegacyWebPanel.Modules.ServerManagement.Configuration;
using AlegacyWebPanel.Modules.ServerManagement.Contracts;
using AlegacyWebPanel.Modules.ServerManagement.Exceptions;
using AlegacyWebPanel.Modules.ServerManagement.Persistence;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.ServerManagement.Services;

public sealed class ServerManagementService(
    IServerRepository serverRepository,
    IRemoteOperationsService remoteOperations,
    IServerLifecycleOperationGuard operationGuard,
    IOptions<ServerManagementOptions> options,
    ILogger<ServerManagementService> logger) : IServerManagementService
{
    private readonly int _maximumCommandLength = options.Value.MaximumCommandLength;

    public async Task<IReadOnlyList<ServerSummary>> ListAsync(CancellationToken cancellationToken) =>
        (await serverRepository.ListAsync(cancellationToken))
        .Select(server => new ServerSummary(server.Id, server.Name, server.Host, server.Port, server.Location))
        .ToArray();

    public async Task<ServerStatusResponse> GetStatusAsync(
        string serverId,
        CancellationToken cancellationToken)
    {
        var server = await FindServerAsync(serverId, cancellationToken);
        return new ServerStatusResponse(server.Id, await ReadStatusAsync(server, cancellationToken));
    }

    public async Task<ServerLifecycleResponse> ExecuteLifecycleAsync(
        string serverId,
        ServerLifecycleAction action,
        CancellationToken cancellationToken)
    {
        var server = await FindServerAsync(serverId, cancellationToken);
        if (!operationGuard.TryEnter(server.Id, out var lease))
        {
            throw new ServerOperationConflictException(server.Id);
        }

        using (lease)
        {
            var operation = action switch
            {
                ServerLifecycleAction.Start => server.StartOperation,
                ServerLifecycleAction.Stop => server.StopOperation,
                ServerLifecycleAction.Restart => server.RestartOperation,
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported lifecycle action.")
            };

            logger.LogInformation("Executing {Action} for server {ServerId}", action, server.Id);
            await ExecuteRequiredAsync(operation, [], cancellationToken);
            var status = await ReadStatusAsync(server, cancellationToken);
            return new ServerLifecycleResponse(server.Id, action, status);
        }
    }

    public async Task<SendServerCommandResponse> SendCommandAsync(
        string serverId,
        string command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command);
        var server = await FindServerAsync(serverId, cancellationToken);
        if (await ReadStatusAsync(server, cancellationToken) != ServerRuntimeStatus.Online)
        {
            logger.LogWarning("Console command rejected for server {ServerId} because it is offline", serverId);
            throw new ServerUnavailableException("The server is offline or its status is unavailable.");
        }

        await ExecuteRequiredAsync(server.ConsoleOperation, [command], cancellationToken);
        return new SendServerCommandResponse(server.Id, Accepted: true);
    }

    public async Task<ServerMetricsResponse> GetMetricsAsync(
        string serverId,
        CancellationToken cancellationToken)
    {
        var server = await FindServerAsync(serverId, cancellationToken);
        var result = await ExecuteRequiredAsync(server.MetricsOperation, [], cancellationToken);
        try
        {
            var metrics = JsonSerializer.Deserialize<MetricsOperationResult>(result.StandardOutput,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (metrics is null)
            {
                throw new JsonException();
            }

            return new ServerMetricsResponse(
                server.Id,
                metrics.CpuPercent,
                metrics.MemoryUsage ?? string.Empty,
                metrics.MemoryLimit ?? string.Empty,
                metrics.MemoryPercent,
                metrics.BlockRead ?? string.Empty,
                metrics.BlockWrite ?? string.Empty,
                metrics.DiskUsedBytes,
                metrics.DiskTotalBytes,
                metrics.DiskAvailableBytes,
                metrics.DiskPercent);
        }
        catch (JsonException)
        {
            logger.LogWarning("Failed to parse metrics for server {ServerId}", server.Id);
            throw new InvalidServerMetricsException();
        }
    }

    public async Task<IAsyncEnumerable<ServerLogEvent>> OpenLogStreamAsync(
        string serverId,
        CancellationToken cancellationToken)
    {
        var server = await FindServerAsync(serverId, cancellationToken);
        return StreamLogsAsync(server.LogsOperation, cancellationToken);
    }

    private async IAsyncEnumerable<ServerLogEvent> StreamLogsAsync(
        string operation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var enumerator = remoteOperations
            .StreamAsync(operation, [], cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            RemoteOperationOutput? output = null;
            ServerLogEvent? failure = null;
            var hasNext = false;
            try
            {
                hasNext = await enumerator.MoveNextAsync();
                output = hasNext ? enumerator.Current : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (RemoteOperationNotAllowedException)
            {
                failure = new ServerLogEvent(ServerLogEventKind.Error, "The server log stream is not configured.");
            }
            catch (RemoteConfigurationException)
            {
                failure = new ServerLogEvent(ServerLogEventKind.Error, "The server log stream is unavailable.");
            }

            if (failure is not null)
            {
                yield return failure;
                yield break;
            }

            if (!hasNext || output is null)
            {
                yield break;
            }

            if (output.Kind == RemoteOperationOutputKind.StandardOutput)
            {
                yield return new ServerLogEvent(ServerLogEventKind.Line, output.Data ?? string.Empty);
            }
            else if (output.Kind == RemoteOperationOutputKind.StandardError)
            {
                yield return new ServerLogEvent(ServerLogEventKind.Error, "The server log stream reported an error.");
            }
            else if (output.Kind == RemoteOperationOutputKind.Completed)
            {
                if (output.ExitStatus != 0)
                {
                    yield return new ServerLogEvent(ServerLogEventKind.Error, "The server log stream stopped unexpectedly.");
                }

                yield return new ServerLogEvent(ServerLogEventKind.End);
                yield break;
            }
        }
    }

    private async Task<ServerRuntimeStatus> ReadStatusAsync(
        ServerDefinition server,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteRequiredAsync(server.StatusOperation, [], cancellationToken);
        var status = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return status?.ToLowerInvariant() switch
        {
            "online" => ServerRuntimeStatus.Online,
            "offline" => ServerRuntimeStatus.Offline,
            _ => ServerRuntimeStatus.Unknown
        };
    }

    private async Task<ExecuteRemoteOperationResponse> ExecuteRequiredAsync(
        string operation,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            throw new ServerUnavailableException("A required server operation is not configured.");
        }

        try
        {
            var result = await remoteOperations.ExecuteAsync(operation, arguments, cancellationToken);
            return result.ExitStatus == 0
                ? result
                : throw new ServerOperationFailedException(operation);
        }
        catch (RemoteOperationNotAllowedException)
        {
            throw new ServerUnavailableException("A required server operation is not available.");
        }
        catch (RemoteConfigurationException)
        {
            throw new ServerUnavailableException("The server execution target is unavailable.");
        }
    }

    private async Task<ServerDefinition> FindServerAsync(string serverId, CancellationToken cancellationToken) =>
        await serverRepository.FindAsync(serverId, cancellationToken) ?? throw new ServerNotFoundException(serverId);

    private void ValidateCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidServerCommandException("A console command is required.");
        }

        if (_maximumCommandLength <= 0 || command.Length > _maximumCommandLength)
        {
            throw new InvalidServerCommandException("The console command is too long.");
        }

        if (command.Any(char.IsControl))
        {
            throw new InvalidServerCommandException("The console command contains unsupported control characters.");
        }
    }

    private sealed record MetricsOperationResult(
        decimal CpuPercent,
        string? MemoryUsage,
        string? MemoryLimit,
        decimal MemoryPercent,
        string? BlockRead,
        string? BlockWrite,
        long DiskUsedBytes,
        long DiskTotalBytes,
        long DiskAvailableBytes,
        decimal DiskPercent);
}
