using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.ServerManagement.Contracts;
using AlegacyWebPanel.Modules.ServerManagement.Endpoints;
using AlegacyWebPanel.Modules.ServerManagement.Exceptions;
using AlegacyWebPanel.Modules.ServerManagement.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AlegacyWebPanel.ServerManagement.FeatureTests;

public sealed class ServerManagementEndpointTests
{
    [Fact]
    public async Task Lifecycle_forwards_route_action_and_returns_service_response()
    {
        var service = new FakeService();

        var result = await ServerManagementEndpoints.LifecycleAsync(
            "main", ServerLifecycleAction.Stop, service, CancellationToken.None);

        var response = Assert.IsType<Ok<ServerLifecycleResponse>>(result);
        Assert.Equal(ServerLifecycleAction.Stop, service.LifecycleAction);
        Assert.Equal(ServerRuntimeStatus.Offline, response.Value!.Status);
    }

    [Fact]
    public async Task Command_maps_request_text_to_service()
    {
        var service = new FakeService();

        await ServerManagementEndpoints.CommandAsync(
            "main",
            new SendServerCommandRequest("/stats"),
            service,
            CancellationToken.None);

        Assert.Equal("/stats", service.Command);
    }

    [Theory]
    [InlineData(Failure.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(Failure.InvalidCommand, StatusCodes.Status400BadRequest)]
    [InlineData(Failure.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(Failure.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(Failure.Operation, StatusCodes.Status502BadGateway)]
    [InlineData(Failure.Metrics, StatusCodes.Status502BadGateway)]
    public async Task Status_translates_domain_failures(Failure failure, int expectedStatus)
    {
        var exception = await Assert.ThrowsAsync<HttpException>(() =>
            ServerManagementEndpoints.StatusAsync(
                "main", new FakeService { Failure = failure }, CancellationToken.None));

        Assert.Equal(expectedStatus, exception.StatusCode);
    }

    [Fact]
    public async Task Logs_translates_unknown_server_before_creating_stream_result()
    {
        var exception = await Assert.ThrowsAsync<HttpException>(() =>
            ServerManagementEndpoints.LogsAsync(
                "missing", new FakeService { Failure = Failure.NotFound }, CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
    }

    public enum Failure
    {
        None,
        NotFound,
        InvalidCommand,
        Conflict,
        Unavailable,
        Operation,
        Metrics
    }

    private sealed class FakeService : IServerManagementService
    {
        public Failure Failure { get; init; }
        public ServerLifecycleAction? LifecycleAction { get; private set; }
        public string? Command { get; private set; }

        public Task<IReadOnlyList<ServerSummary>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServerSummary>>([]);

        public Task<ServerStatusResponse> GetStatusAsync(string serverId, CancellationToken cancellationToken)
        {
            ThrowIfRequested(serverId);
            return Task.FromResult(new ServerStatusResponse(serverId, ServerRuntimeStatus.Online));
        }

        public Task<ServerLifecycleResponse> ExecuteLifecycleAsync(
            string serverId,
            ServerLifecycleAction action,
            CancellationToken cancellationToken)
        {
            ThrowIfRequested(serverId);
            LifecycleAction = action;
            return Task.FromResult(new ServerLifecycleResponse(serverId, action, ServerRuntimeStatus.Offline));
        }

        public Task<SendServerCommandResponse> SendCommandAsync(
            string serverId,
            string command,
            CancellationToken cancellationToken)
        {
            ThrowIfRequested(serverId);
            Command = command;
            return Task.FromResult(new SendServerCommandResponse(serverId, true));
        }

        public Task<ServerMetricsResponse> GetMetricsAsync(string serverId, CancellationToken cancellationToken)
        {
            ThrowIfRequested(serverId);
            return Task.FromResult(new ServerMetricsResponse(
                serverId, 0, "", "", 0, "", "", 0, 0, 0, 0));
        }

        public Task<IAsyncEnumerable<ServerLogEvent>> OpenLogStreamAsync(
            string serverId,
            CancellationToken cancellationToken)
        {
            ThrowIfRequested(serverId);
            return Task.FromResult(EmptyLogs());
        }

        private void ThrowIfRequested(string serverId)
        {
            if (Failure == Failure.None)
            {
                return;
            }

            throw Failure switch
            {
                Failure.NotFound => new ServerNotFoundException(serverId),
                Failure.InvalidCommand => new InvalidServerCommandException("invalid"),
                Failure.Conflict => new ServerOperationConflictException(serverId),
                Failure.Unavailable => new ServerUnavailableException("unavailable"),
                Failure.Operation => new ServerOperationFailedException("operation"),
                Failure.Metrics => new InvalidServerMetricsException(),
                _ => new ArgumentOutOfRangeException()
            };
        }

        private static async IAsyncEnumerable<ServerLogEvent> EmptyLogs()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
