using AlegacyWebPanel.Modules.RemoteOperations.Contracts;
using AlegacyWebPanel.Modules.RemoteOperations.Infrastructure;

namespace AlegacyWebPanel.RemoteOperations.UnitTests;

public sealed class RemoteConnectionArgumentTests
{
    [Fact]
    public void Local_connection_keeps_dynamic_argument_separate_from_executable()
    {
        var definition = new RemoteCommandDefinition(
            "command",
            "/opt/server-control.sh",
            new RemoteExecutionTargetDefinition("local", ExecutionMode.Local),
            Arguments: ["command", "--"]);

        var startInfo = LocalRemoteConnection.CreateStartInfo(
            definition,
            ["/announce hello; touch /tmp/unwanted"]);

        Assert.Equal("/opt/server-control.sh", startInfo.FileName);
        Assert.Equal(
            ["command", "--", "/announce hello; touch /tmp/unwanted"],
            startInfo.ArgumentList);
    }

    [Fact]
    public void Ssh_connection_posix_quotes_every_configured_and_dynamic_token()
    {
        var definition = new RemoteCommandDefinition(
            "command",
            "/opt/server control.sh",
            new RemoteExecutionTargetDefinition(
                "remote", ExecutionMode.Ssh, "host", 22, "user", "/key", null, "SHA256:test"),
            Arguments: ["command", "--"]);

        var command = SshRemoteConnection.BuildCommand(
            definition,
            ["player's message; shutdown now"]);

        Assert.Equal(
            "'/opt/server control.sh' 'command' '--' 'player'\"'\"'s message; shutdown now'",
            command);
    }
}
