using AlegacyWebPanel.Modules.RemoteOperations.Contracts;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using AlegacyWebPanel.Modules.RemoteOperations.Infrastructure;
using AlegacyWebPanel.Modules.RemoteOperations.Persistence;
using AlegacyWebPanel.Modules.RemoteOperations.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlegacyWebPanel.RemoteOperations.UnitTests;

public sealed class RemoteOperationsServiceTests
{
    [Fact]
    public async Task Execute_uses_repository_definition_and_connection()
    {
        var definition = new RemoteCommandDefinition("status", "/usr/bin/systemctl", LocalTarget);
        var repository = new FakeCommandRepository(definition);
        var connection = new FakeRemoteConnection();
        var factory = new FakeRemoteConnectionFactory(connection);
        var service = new RemoteOperationsService(repository, factory, NullLogger<RemoteOperationsService>.Instance);

        var result = await service.ExecuteAsync("status", CancellationToken.None);

        Assert.Equal(0, result.ExitStatus);
        Assert.Equal("/usr/bin/systemctl", connection.ExecutedCommand);
        Assert.Equal("local-one", factory.SelectedTarget?.Name);
    }

    [Fact]
    public async Task Execute_forwards_structured_arguments_without_merging_them_into_command()
    {
        var definition = new RemoteCommandDefinition(
            "command", "/opt/server-control.sh", LocalTarget, Arguments: ["command", "--"]);
        var connection = new FakeRemoteConnection();
        var service = new RemoteOperationsService(
            new FakeCommandRepository(definition),
            new FakeRemoteConnectionFactory(connection),
            NullLogger<RemoteOperationsService>.Instance);

        await service.ExecuteAsync("command", ["/announce hello; shutdown now"], CancellationToken.None);

        Assert.Equal("/opt/server-control.sh", connection.ExecutedCommand);
        Assert.Equal(["/announce hello; shutdown now"], connection.ExecutedArguments);
    }

    [Fact]
    public async Task Stream_forwards_connection_output_and_cancellation()
    {
        var cancellation = new CancellationTokenSource();
        var connection = new FakeRemoteConnection();
        var service = new RemoteOperationsService(
            new FakeCommandRepository(new RemoteCommandDefinition(
                "logs", "/opt/server-control.sh", SshTarget, Arguments: ["logs"])),
            new FakeRemoteConnectionFactory(connection),
            NullLogger<RemoteOperationsService>.Instance);

        var output = new List<RemoteOperationOutput>();
        await foreach (var item in service.StreamAsync("logs", [], cancellation.Token))
        {
            output.Add(item);
        }

        Assert.Equal(RemoteOperationOutputKind.StandardOutput, output[0].Kind);
        Assert.Equal("line", output[0].Data);
        Assert.Equal(RemoteOperationOutputKind.Completed, output[1].Kind);
        Assert.Equal(cancellation.Token, connection.StreamCancellationToken);
    }

    [Fact]
    public async Task ExecuteBinary_uses_repository_definition_and_connection()
    {
        var definition = new RemoteCommandDefinition("binary-op", "/usr/bin/cat", LocalTarget);
        var repository = new FakeCommandRepository(definition);
        var connection = new FakeRemoteConnection();
        var factory = new FakeRemoteConnectionFactory(connection);
        var service = new RemoteOperationsService(repository, factory, NullLogger<RemoteOperationsService>.Instance);

        using var stdin = new MemoryStream();
        using var stdout = new MemoryStream();

        await service.ExecuteBinaryAsync("binary-op", ["arg1"], stdin, stdout, CancellationToken.None);

        Assert.Equal("/usr/bin/cat", connection.ExecutedBinaryCommand);
        Assert.Equal(["arg1"], connection.ExecutedBinaryArguments);
        Assert.Same(stdin, connection.InputBinaryStream);
        Assert.Same(stdout, connection.OutputBinaryStream);
        Assert.Equal("local-one", factory.SelectedTarget?.Name);
    }

    [Fact]
    public async Task Execute_rejects_operations_not_returned_by_repository()
    {
        var service = new RemoteOperationsService(
            new FakeCommandRepository(null),
            new FakeRemoteConnectionFactory(new FakeRemoteConnection()),
            NullLogger<RemoteOperationsService>.Instance);

        await Assert.ThrowsAsync<RemoteOperationNotAllowedException>(() =>
            service.ExecuteAsync("delete-everything", CancellationToken.None));
    }

    private sealed class FakeCommandRepository(RemoteCommandDefinition? definition) : IRemoteCommandRepository
    {
        public Task<RemoteCommandDefinition?> FindAsync(string operation, CancellationToken cancellationToken) =>
            Task.FromResult(definition?.Operation == operation ? definition : null);
    }

    private sealed class FakeRemoteConnectionFactory(IRemoteConnection connection) : IRemoteConnectionFactory
    {
        public RemoteExecutionTargetDefinition? SelectedTarget { get; private set; }

        public IRemoteConnection GetConnection(RemoteExecutionTargetDefinition target)
        {
            SelectedTarget = target;
            return connection;
        }
    }

    private sealed class FakeRemoteConnection : IRemoteConnection
    {
        public string? ExecutedCommand { get; private set; }
        public IReadOnlyList<string>? ExecutedArguments { get; private set; }
        public CancellationToken StreamCancellationToken { get; private set; }

        public string? ExecutedBinaryCommand { get; private set; }
        public IReadOnlyList<string>? ExecutedBinaryArguments { get; private set; }
        public Stream? InputBinaryStream { get; private set; }
        public Stream? OutputBinaryStream { get; private set; }

        public Task<RemoteConnectionResult> ExecuteAsync(
            RemoteCommandDefinition definition,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            ExecutedCommand = definition.Command;
            ExecutedArguments = arguments;
            return Task.FromResult(new RemoteConnectionResult(0, "ok", string.Empty));
        }

        public async IAsyncEnumerable<RemoteConnectionOutput> StreamAsync(
            RemoteCommandDefinition definition,
            IReadOnlyList<string> arguments,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            StreamCancellationToken = cancellationToken;
            await Task.Yield();
            yield return new RemoteConnectionOutput(RemoteOperationOutputKind.StandardOutput, "line");
            yield return new RemoteConnectionOutput(RemoteOperationOutputKind.Completed, ExitStatus: 0);
        }

        public Task ExecuteBinaryAsync(
            RemoteCommandDefinition definition,
            IReadOnlyList<string> arguments,
            Stream? stdin,
            Stream? stdout,
            CancellationToken cancellationToken)
        {
            ExecutedBinaryCommand = definition.Command;
            ExecutedBinaryArguments = arguments;
            InputBinaryStream = stdin;
            OutputBinaryStream = stdout;
            return Task.CompletedTask;
        }
    }

    private static readonly RemoteExecutionTargetDefinition LocalTarget =
        new("local-one", ExecutionMode.Local);

    private static readonly RemoteExecutionTargetDefinition SshTarget =
        new("remote-one", ExecutionMode.Ssh, "one.example", 22, "panel", "/key", null, "SHA256:test");
}
