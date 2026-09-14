using AlegacyWebPanel.Modules.RemoteOperations.Contracts;
using AlegacyWebPanel.Modules.RemoteOperations.Services;
using AlegacyWebPanel.Modules.ServerManagement.Configuration;
using AlegacyWebPanel.Modules.ServerManagement.Contracts;
using AlegacyWebPanel.Modules.ServerManagement.Exceptions;
using AlegacyWebPanel.Modules.ServerManagement.Persistence;
using AlegacyWebPanel.Modules.ServerManagement.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.ServerManagement.UnitTests;

public sealed class ServerManagementServiceTests
{
    [Fact]
    public async Task Lifecycle_maps_action_and_refreshes_authoritative_status()
    {
        var remote = new FakeRemoteOperations();
        remote.Results["restart-op"] = Success();
        remote.Results["status-op"] = Success("online\n");
        var service = CreateService(remote: remote);

        var result = await service.ExecuteLifecycleAsync("main", ServerLifecycleAction.Restart, CancellationToken.None);

        Assert.Equal(ServerRuntimeStatus.Online, result.Status);
        Assert.Equal(["restart-op", "status-op"], remote.ExecutedOperations);
    }

    [Fact]
    public async Task Lifecycle_rejects_conflicting_operation_for_same_server()
    {
        var service = CreateService(guard: new RejectingGuard());

        await Assert.ThrowsAsync<ServerOperationConflictException>(() =>
            service.ExecuteLifecycleAsync("main", ServerLifecycleAction.Start, CancellationToken.None));
    }

    [Fact]
    public async Task Lifecycle_rejects_nonzero_remote_exit_status()
    {
        var remote = new FakeRemoteOperations();
        remote.Results["start-op"] = new ExecuteRemoteOperationResponse(1, string.Empty, "failed");
        var service = CreateService(remote: remote);

        await Assert.ThrowsAsync<ServerOperationFailedException>(() =>
            service.ExecuteLifecycleAsync("main", ServerLifecycleAction.Start, CancellationToken.None));
    }

    [Fact]
    public async Task Lookup_rejects_unknown_server_id()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ServerNotFoundException>(() =>
            service.GetStatusAsync("missing", CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("line one\nline two")]
    [InlineData("bad\0command")]
    public async Task Command_rejects_invalid_console_text(string command)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidServerCommandException>(() =>
            service.SendCommandAsync("main", command, CancellationToken.None));
    }

    [Fact]
    public async Task Command_forwards_original_text_as_one_argument_when_online()
    {
        var remote = new FakeRemoteOperations();
        remote.Results["status-op"] = Success("online");
        remote.Results["console-op"] = Success();
        var service = CreateService(remote: remote);
        const string command = "/announce Keep  original  spacing";

        var result = await service.SendCommandAsync("main", command, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal([command], remote.ExecutedArguments["console-op"]);
    }

    [Fact]
    public async Task Metrics_maps_json_operation_response()
    {
        var remote = new FakeRemoteOperations();
        remote.Results["metrics-op"] = Success("""
            {"cpuPercent":12.5,"memoryUsage":"1GiB","memoryLimit":"2GiB","memoryPercent":50,
             "blockRead":"3MB","blockWrite":"4MB","diskUsedBytes":10,"diskTotalBytes":100,
             "diskAvailableBytes":90,"diskPercent":10}
            """);
        var service = CreateService(remote: remote);

        var metrics = await service.GetMetricsAsync("main", CancellationToken.None);

        Assert.Equal(12.5m, metrics.CpuPercent);
        Assert.Equal(100, metrics.DiskTotalBytes);
    }

    [Fact]
    public async Task Logs_map_stdout_and_do_not_expose_stderr()
    {
        var remote = new FakeRemoteOperations
        {
            StreamOutput =
            [
                new RemoteOperationOutput(RemoteOperationOutputKind.StandardOutput, "server line"),
                new RemoteOperationOutput(RemoteOperationOutputKind.StandardError, "/private/path failed"),
                new RemoteOperationOutput(RemoteOperationOutputKind.Completed, ExitStatus: 0)
            ]
        };
        var service = CreateService(remote: remote);
        var stream = await service.OpenLogStreamAsync("main", CancellationToken.None);
        var events = new List<ServerLogEvent>();

        await foreach (var logEvent in stream)
        {
            events.Add(logEvent);
        }

        Assert.Equal("server line", events[0].Data);
        Assert.DoesNotContain("/private/path", events[1].Data);
        Assert.Equal(ServerLogEventKind.End, events[2].Kind);
    }

    private static ServerManagementService CreateService(
        FakeRemoteOperations? remote = null,
        IServerLifecycleOperationGuard? guard = null) => new(
        new FakeServerRepository(Server),
        remote ?? new FakeRemoteOperations(),
        guard ?? new ServerLifecycleOperationGuard(),
        Options.Create(new ServerManagementOptions { MaximumCommandLength = 512 }),
        NullLogger<ServerManagementService>.Instance);

    private static ExecuteRemoteOperationResponse Success(string output = "") => new(0, output, string.Empty);

    private static readonly ServerDefinition Server = new(
        "main", "Main", "localhost", 42420, "Local",
        "start-op", "stop-op", "restart-op", "status-op", "console-op", "logs-op", "metrics-op");

    private sealed class FakeServerRepository(params ServerDefinition[] servers) : IServerRepository
    {
        public Task<IReadOnlyList<ServerDefinition>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServerDefinition>>(servers);

        public Task<ServerDefinition?> FindAsync(string serverId, CancellationToken cancellationToken) =>
            Task.FromResult(servers.FirstOrDefault(server => server.Id == serverId));
    }

    private sealed class FakeRemoteOperations : IRemoteOperationsService
    {
        public Dictionary<string, ExecuteRemoteOperationResponse> Results { get; } = [];
        public List<string> ExecutedOperations { get; } = [];
        public Dictionary<string, IReadOnlyList<string>> ExecutedArguments { get; } = [];
        public IReadOnlyList<RemoteOperationOutput> StreamOutput { get; init; } = [];

        public Task<ExecuteRemoteOperationResponse> ExecuteAsync(string operation, CancellationToken cancellationToken) =>
            ExecuteAsync(operation, [], cancellationToken);

        public Task<ExecuteRemoteOperationResponse> ExecuteAsync(
            string operation,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            ExecutedOperations.Add(operation);
            ExecutedArguments[operation] = arguments;
            return Task.FromResult(Results.TryGetValue(operation, out var result) ? result : Success());
        }

        public async IAsyncEnumerable<RemoteOperationOutput> StreamAsync(
            string operation,
            IReadOnlyList<string> arguments,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var output in StreamOutput)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return output;
            }
        }

        public Task ExecuteBinaryAsync(
            string operation,
            IReadOnlyList<string> arguments,
            Stream? stdin,
            Stream? stdout,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RejectingGuard : IServerLifecycleOperationGuard
    {
        public bool TryEnter(string serverId, out IDisposable lease)
        {
            lease = new EmptyLease();
            return false;
        }

        private sealed class EmptyLease : IDisposable { public void Dispose() { } }
    }
}
