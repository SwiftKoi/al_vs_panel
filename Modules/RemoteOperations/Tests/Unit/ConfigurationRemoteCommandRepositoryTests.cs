using AlegacyWebPanel.Modules.RemoteOperations.Configuration;
using AlegacyWebPanel.Modules.RemoteOperations.Contracts;
using AlegacyWebPanel.Modules.RemoteOperations.Exceptions;
using AlegacyWebPanel.Modules.RemoteOperations.Persistence;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.RemoteOperations.UnitTests;

public sealed class ConfigurationRemoteCommandRepositoryTests
{
    [Fact]
    public async Task Find_resolves_target_specific_connection_settings()
    {
        var options = new RemoteCommandOptions
        {
            Targets =
            {
                ["remote-eu"] = new RemoteTargetConfig
                {
                    Mode = ExecutionMode.Ssh,
                    Host = "eu.example",
                    Port = 2222,
                    Username = "panel-eu",
                    PrivateKeyFile = "/keys/eu",
                    HostKeyFingerprintSha256 = "SHA256:eu"
                }
            },
            Commands =
            {
                ["eu-status"] = new RemoteCommandConfig
                {
                    Target = "remote-eu",
                    Command = "/opt/server-control.sh",
                    Arguments = ["status"]
                }
            }
        };
        var repository = new ConfigurationRemoteCommandRepository(Options.Create(options));

        var definition = await repository.FindAsync("eu-status", CancellationToken.None);

        Assert.NotNull(definition);
        Assert.Equal("remote-eu", definition.Target.Name);
        Assert.Equal("eu.example", definition.Target.Host);
        Assert.Equal(2222, definition.Target.Port);
        Assert.Equal("panel-eu", definition.Target.Username);
        Assert.Equal("/keys/eu", definition.Target.PrivateKeyFile);
    }

    [Fact]
    public async Task Find_rejects_command_with_unknown_target()
    {
        var options = new RemoteCommandOptions
        {
            Commands =
            {
                ["status"] = new RemoteCommandConfig
                {
                    Target = "missing",
                    Command = "/opt/server-control.sh"
                }
            }
        };
        var repository = new ConfigurationRemoteCommandRepository(Options.Create(options));

        await Assert.ThrowsAsync<RemoteConfigurationException>(() =>
            repository.FindAsync("status", CancellationToken.None));
    }
}
