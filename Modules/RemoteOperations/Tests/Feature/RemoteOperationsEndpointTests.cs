using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.RemoteOperations.Contracts;
using AlegacyWebPanel.Modules.RemoteOperations.Endpoints;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using AlegacyWebPanel.Modules.RemoteOperations.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AlegacyWebPanel.RemoteOperations.FeatureTests;

public sealed class RemoteOperationsEndpointTests
{
    [Fact]
    public async Task Execute_returns_service_response()
    {
        var result = await RemoteOperationsEndpoints.ExecuteAsync(
            "status",
            new FakeRemoteOperationsService(),
            CancellationToken.None);

        var response = Assert.IsType<Ok<ExecuteRemoteOperationResponse>>(result);
        Assert.Equal(0, response.Value!.ExitStatus);
    }

    [Fact]
    public async Task Execute_translates_not_allowed_domain_failure_to_http_exception()
    {
        await Assert.ThrowsAsync<HttpException>(() => RemoteOperationsEndpoints.ExecuteAsync(
            "unknown",
            new FailingRemoteOperationsService(),
            CancellationToken.None));
    }

    private sealed class FakeRemoteOperationsService : IRemoteOperationsService
    {
        public Task<ExecuteRemoteOperationResponse> ExecuteAsync(string operation, CancellationToken cancellationToken) =>
            Task.FromResult(new ExecuteRemoteOperationResponse(0, "ok", string.Empty));

        public Task<ExecuteRemoteOperationResponse> ExecuteAsync(
            string operation,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) => ExecuteAsync(operation, cancellationToken);

        public IAsyncEnumerable<RemoteOperationOutput> StreamAsync(
            string operation,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) => EmptyOutput();

        public Task ExecuteBinaryAsync(
            string operation,
            IReadOnlyList<string> arguments,
            Stream? stdin,
            Stream? stdout,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FailingRemoteOperationsService : IRemoteOperationsService
    {
        public Task<ExecuteRemoteOperationResponse> ExecuteAsync(string operation, CancellationToken cancellationToken) =>
            throw new RemoteOperationNotAllowedException(operation);

        public Task<ExecuteRemoteOperationResponse> ExecuteAsync(
            string operation,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) => ExecuteAsync(operation, cancellationToken);

        public IAsyncEnumerable<RemoteOperationOutput> StreamAsync(
            string operation,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) => EmptyOutput();

        public Task ExecuteBinaryAsync(
            string operation,
            IReadOnlyList<string> arguments,
            Stream? stdin,
            Stream? stdout,
            CancellationToken cancellationToken) => throw new RemoteOperationNotAllowedException(operation);
    }

    private static async IAsyncEnumerable<RemoteOperationOutput> EmptyOutput()
    {
        await Task.CompletedTask;
        yield break;
    }
}
