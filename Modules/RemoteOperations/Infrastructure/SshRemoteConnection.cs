using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using AlegacyWebPanel.Core.Abstractions;
using AlegacyWebPanel.Modules.RemoteOperations.Contracts;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using Renci.SshNet;

namespace AlegacyWebPanel.Modules.RemoteOperations.Infrastructure;

public sealed class SshRemoteConnection(ISecretReader secretReader)
    : IRemoteConnection
{
    private readonly ISecretReader _secretReader = secretReader;

    public async Task<RemoteConnectionResult> ExecuteAsync(
        RemoteCommandDefinition definition,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var standardOutput = new StringBuilder();
        var errorOutput = new StringBuilder();
        var exitStatus = -1;

        await foreach (var output in StreamAsync(definition, arguments, cancellationToken))
        {
            switch (output.Kind)
            {
                case RemoteOperationOutputKind.StandardOutput:
                    standardOutput.AppendLine(output.Data);
                    break;
                case RemoteOperationOutputKind.StandardError:
                    errorOutput.AppendLine(output.Data);
                    break;
                case RemoteOperationOutputKind.Completed:
                    exitStatus = output.ExitStatus ?? -1;
                    break;
            }
        }

        return new RemoteConnectionResult(exitStatus, standardOutput.ToString(), errorOutput.ToString());
    }

    public async IAsyncEnumerable<RemoteConnectionOutput> StreamAsync(
        RemoteCommandDefinition definition,
        IReadOnlyList<string> arguments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ValidateTarget(definition.Target);
        cancellationToken.ThrowIfCancellationRequested();

        using var privateKeyStream = CreatePrivateKeyStream(definition.Target, out var passphrase);
        using var privateKeyFile = passphrase is null
            ? new PrivateKeyFile(privateKeyStream)
            : new PrivateKeyFile(privateKeyStream, passphrase);
        using var client = CreateClient(definition.Target, privateKeyFile);

        await client.ConnectAsync(cancellationToken);
        using var command = client.CreateCommand(BuildCommand(definition, arguments));
        var output = Channel.CreateUnbounded<RemoteConnectionOutput>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var execution = CompleteCommandAsync(command, output.Writer, cancellationToken);
        await foreach (var item in output.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }

        await execution;
        client.Disconnect();
    }

    internal static string BuildCommand(
        RemoteCommandDefinition definition,
        IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(definition.Command))
        {
            throw new ArgumentException("A configured command is required.", nameof(definition));
        }

        var tokens = new List<string> { definition.Command };
        tokens.AddRange(definition.Arguments ?? []);
        tokens.AddRange(arguments);
        var command = string.Join(' ', tokens.Select(QuotePosixArgument));

        return string.IsNullOrWhiteSpace(definition.User)
            ? command
            : $"su -s /bin/sh -c {QuotePosixArgument(command)} {QuotePosixArgument(definition.User)}";
    }

    internal static string QuotePosixArgument(string value) =>
        $"'{value.Replace("'", "'\"'\"'")}'";

    private async Task CompleteCommandAsync(
        SshCommand command,
        ChannelWriter<RemoteConnectionOutput> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            var executeTask = command.ExecuteAsync(cancellationToken);
            var standardOutputTask = ReadLinesAsync(
                command.OutputStream,
                RemoteOperationOutputKind.StandardOutput,
                writer,
                cancellationToken);
            var errorOutputTask = ReadLinesAsync(
                command.ExtendedOutputStream,
                RemoteOperationOutputKind.StandardError,
                writer,
                cancellationToken);

            await executeTask;
            await Task.WhenAll(standardOutputTask, errorOutputTask);
            writer.TryWrite(new RemoteConnectionOutput(
                RemoteOperationOutputKind.Completed,
                ExitStatus: command.ExitStatus ?? -1));
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
    }

    private static async Task ReadLinesAsync(
        Stream stream,
        RemoteOperationOutputKind kind,
        ChannelWriter<RemoteConnectionOutput> writer,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            await writer.WriteAsync(new RemoteConnectionOutput(kind, line), cancellationToken);
        }
    }

    private MemoryStream CreatePrivateKeyStream(
        RemoteExecutionTargetDefinition target,
        out string? passphrase)
    {
        passphrase = string.IsNullOrWhiteSpace(target.PrivateKeyPassphraseFile)
            ? null
            : _secretReader.ReadRequired(
                target.PrivateKeyPassphraseFile,
                "SSH private key passphrase").Value;
        var privateKey = _secretReader.ReadRequired(target.PrivateKeyFile, "SSH private key");
        return new MemoryStream(Encoding.UTF8.GetBytes(privateKey.Value));
    }

    private SshClient CreateClient(
        RemoteExecutionTargetDefinition target,
        PrivateKeyFile privateKeyFile)
    {
        var authentication = new PrivateKeyAuthenticationMethod(target.Username, privateKeyFile);
        var connectionInfo = new Renci.SshNet.ConnectionInfo(
            target.Host,
            target.Port,
            target.Username,
            authentication)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        var client = new SshClient(connectionInfo);
        client.HostKeyReceived += (_, eventArgs) =>
        {
            var expected = Encoding.UTF8.GetBytes(target.HostKeyFingerprintSha256);
            var actual = Encoding.UTF8.GetBytes(eventArgs.FingerPrintSHA256);
            eventArgs.CanTrust = expected.Length == actual.Length &&
                                 CryptographicOperations.FixedTimeEquals(expected, actual);
        };
        return client;
    }

    private static void ValidateTarget(RemoteExecutionTargetDefinition target)
    {
        if (target.Mode != ExecutionMode.Ssh ||
            string.IsNullOrWhiteSpace(target.Host) ||
            string.IsNullOrWhiteSpace(target.Username) ||
            string.IsNullOrWhiteSpace(target.PrivateKeyFile) ||
            string.IsNullOrWhiteSpace(target.HostKeyFingerprintSha256) ||
            target.Port is < 1 or > 65535)
        {
            throw new RemoteConfigurationException(
                "SSH target host, port, username, private key, and SHA-256 host-key fingerprint must be configured.");
        }
    }

    public async Task ExecuteBinaryAsync(
        RemoteCommandDefinition definition,
        IReadOnlyList<string> arguments,
        Stream? stdin,
        Stream? stdout,
        CancellationToken cancellationToken)
    {
        ValidateTarget(definition.Target);
        cancellationToken.ThrowIfCancellationRequested();

        using var privateKeyStream = CreatePrivateKeyStream(definition.Target, out var passphrase);
        using var privateKeyFile = passphrase is null
            ? new PrivateKeyFile(privateKeyStream)
            : new PrivateKeyFile(privateKeyStream, passphrase);
        using var client = CreateClient(definition.Target, privateKeyFile);

        await client.ConnectAsync(cancellationToken);
        using var command = client.CreateCommand(BuildCommand(definition, arguments));

        var tasks = new List<Task>();

        var executeTask = command.ExecuteAsync(cancellationToken);
        tasks.Add(executeTask);

        Task? writeTask = null;
        if (stdin is not null)
        {
            writeTask = Task.Run(async () =>
            {
                try
                {
                    using var inputStream = command.CreateInputStream();
                    await stdin.CopyToAsync(inputStream, cancellationToken);
                    await inputStream.FlushAsync(cancellationToken);
                }
                catch (Exception)
                {
                    // Catch exceptions to prevent crashing if connection terminates early.
                }
            }, cancellationToken);
            tasks.Add(writeTask);
        }

        Task? readOutputTask = null;
        if (stdout is not null)
        {
            readOutputTask = command.OutputStream.CopyToAsync(stdout, cancellationToken);
            tasks.Add(readOutputTask);
        }

        using var errorMs = new MemoryStream();
        var readErrorTask = command.ExtendedOutputStream.CopyToAsync(errorMs, cancellationToken);
        tasks.Add(readErrorTask);

        try
        {
            await executeTask;
            if (writeTask is not null) await writeTask;
            if (readOutputTask is not null) await readOutputTask;
            await readErrorTask;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            client.Disconnect();
        }

        var exitStatus = command.ExitStatus ?? -1;
        if (exitStatus != 0)
        {
            var errorOutput = Encoding.UTF8.GetString(errorMs.ToArray());
            throw new RemoteOperationFailedException(definition.Operation, exitStatus, errorOutput);
        }
    }
}
