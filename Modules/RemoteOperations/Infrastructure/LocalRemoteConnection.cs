using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using AlegacyWebPanel.Modules.RemoteOperations.Contracts;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using Microsoft.Extensions.Logging;

namespace AlegacyWebPanel.Modules.RemoteOperations.Infrastructure;

public sealed class LocalRemoteConnection : IRemoteConnection
{
    private readonly ILogger<LocalRemoteConnection>? _logger;

    public LocalRemoteConnection(ILogger<LocalRemoteConnection>? logger = null)
    {
        _logger = logger;
    }

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
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = CreateStartInfo(definition, arguments);
        using var process = new Process { StartInfo = startInfo };
        var output = Channel.CreateUnbounded<RemoteConnectionOutput>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                output.Writer.TryWrite(new RemoteConnectionOutput(
                    RemoteOperationOutputKind.StandardOutput,
                    eventArgs.Data));
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                output.Writer.TryWrite(new RemoteConnectionOutput(
                    RemoteOperationOutputKind.StandardError,
                    eventArgs.Data));
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completion = CompleteProcessAsync(process, output.Writer, cancellationToken);
        await foreach (var item in output.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }

        await completion;
    }

    internal static ProcessStartInfo CreateStartInfo(
        RemoteCommandDefinition definition,
        IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(definition.Command))
        {
            throw new ArgumentException("A configured command is required.", nameof(definition));
        }

        var allArguments = (definition.Arguments ?? []).Concat(arguments);
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (string.IsNullOrWhiteSpace(definition.User))
        {
            startInfo.FileName = definition.Command;
            foreach (var argument in allArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        // Arguments after the shell program are positional parameters, so none of them are parsed as shell syntax.
        startInfo.FileName = "su";
        startInfo.ArgumentList.Add("-s");
        startInfo.ArgumentList.Add("/bin/sh");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("exec \"$0\" \"$@\"");
        startInfo.ArgumentList.Add(definition.User);
        startInfo.ArgumentList.Add(definition.Command);
        foreach (var argument in allArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task CompleteProcessAsync(
        Process process,
        ChannelWriter<RemoteConnectionOutput> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            process.WaitForExit();
            writer.TryWrite(new RemoteConnectionOutput(
                RemoteOperationOutputKind.Completed,
                ExitStatus: process.ExitCode));
            writer.TryComplete();
        }
        catch (OperationCanceledException exception)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            writer.TryComplete(exception);
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
    }

    public async Task ExecuteBinaryAsync(
        RemoteCommandDefinition definition,
        IReadOnlyList<string> arguments,
        Stream? stdin,
        Stream? stdout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = CreateStartInfo(definition, arguments);
        startInfo.RedirectStandardInput = stdin is not null;
        startInfo.RedirectStandardOutput = stdout is not null;
        startInfo.RedirectStandardError = true;

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var tasks = new List<Task>();

        Task? writeTask = null;
        if (stdin is not null)
        {
            writeTask = Task.Run(async () =>
            {
                try
                {
                    await stdin.CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
                }
                catch (Exception exception)
                {
                    // The data stream failed half-way (size limit exceeded, client
                    // aborted): kill the helper before it sees EOF, otherwise it treats
                    // the truncated bytes as a complete file and renames them over the
                    // real one. Rethrow — this is the error worth reporting.
                    _logger?.LogDebug(exception, "Upload stream failed for {Operation}; killing the helper.",
                        definition.Operation);
                    KillIfRunning(process);
                    throw;
                }
                finally
                {
                    CloseStandardInput(process);
                }
            }, cancellationToken);
            tasks.Add(writeTask);
        }

        var readErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        tasks.Add(readErrorTask);

        Task? readOutputTask = null;
        if (stdout is not null)
        {
            readOutputTask = process.StandardOutput.BaseStream.CopyToAsync(stdout, cancellationToken);
            tasks.Add(readOutputTask);
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception exception) when (process.HasExited && exception is IOException or ObjectDisposedException)
        {
            // A broken pipe from the stdin writer only means the helper closed its end
            // first because it exited. The exit code and stderr below say why, so let
            // them win instead of masking the real reason.
            _logger?.LogDebug(exception, "Helper exited before consuming all of stdin ({Operation}).",
                definition.Operation);
        }

        var exitCode = process.ExitCode;
        var errorOutput = readErrorTask.IsCompletedSuccessfully ? readErrorTask.Result : string.Empty;

        if (exitCode != 0)
        {
            throw new RemoteOperationFailedException(definition.Operation, exitCode, errorOutput);
        }
    }

    /// <summary>Closes the helper's stdin, tolerating one that already exited.</summary>
    private void CloseStandardInput(Process process)
    {
        try
        {
            // Close() flushes; the payload was written straight to the base stream anyway.
            process.StandardInput.Close();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            _logger?.LogDebug(exception, "Stdin was already closed by the helper.");
        }
    }

    /// <summary>Stops the helper, tolerating one that exited on its own in the meantime.</summary>
    private void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            _logger?.LogDebug(exception, "Could not kill the helper; it had already exited.");
        }
    }
}
