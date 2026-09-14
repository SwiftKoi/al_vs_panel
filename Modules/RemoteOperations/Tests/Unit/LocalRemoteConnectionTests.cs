using System.Text;
using AlegacyWebPanel.Modules.RemoteOperations.Contracts;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using AlegacyWebPanel.Modules.RemoteOperations.Infrastructure;

namespace AlegacyWebPanel.RemoteOperations.UnitTests;

public sealed class LocalRemoteConnectionTests
{
    [Fact]
    public async Task ExecuteBinaryAsync_pipes_input_and_captures_output()
    {
        var connection = new LocalRemoteConnection();
        var target = new RemoteExecutionTargetDefinition("local", ExecutionMode.Local);
        var definition = new RemoteCommandDefinition("test-cat", "cat", target);

        var inputText = "Hello, local binary execution!";
        using var stdin = new MemoryStream(Encoding.UTF8.GetBytes(inputText));
        using var stdout = new MemoryStream();

        await connection.ExecuteBinaryAsync(definition, [], stdin, stdout, CancellationToken.None);

        var outputText = Encoding.UTF8.GetString(stdout.ToArray());
        Assert.Equal(inputText, outputText);
    }

    [Fact]
    public async Task ExecuteBinaryAsync_throws_exception_on_nonzero_exit_code()
    {
        var connection = new LocalRemoteConnection();
        var target = new RemoteExecutionTargetDefinition("local", ExecutionMode.Local);
        var definition = new RemoteCommandDefinition("test-ls", "ls", target);

        var exception = await Assert.ThrowsAsync<RemoteOperationFailedException>(() =>
            connection.ExecuteBinaryAsync(definition, ["/nonexistent-directory-xyz"], null, null, CancellationToken.None));

        Assert.Equal("test-ls", exception.Operation);
        Assert.True(exception.ExitStatus != 0);
        Assert.Contains("No such file or directory", exception.ErrorOutput);
    }
}
