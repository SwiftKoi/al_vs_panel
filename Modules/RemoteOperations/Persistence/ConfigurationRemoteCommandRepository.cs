using AlegacyWebPanel.Modules.RemoteOperations.Configuration;
using AlegacyWebPanel.Modules.RemoteOperations.Contracts;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.RemoteOperations.Persistence;

public sealed class ConfigurationRemoteCommandRepository(IOptions<RemoteCommandOptions> options)
    : IRemoteCommandRepository
{
    private readonly RemoteCommandOptions _options = options.Value;

    public Task<RemoteCommandDefinition?> FindAsync(string operation, CancellationToken cancellationToken)
    {
        if (!_options.Commands.TryGetValue(operation, out var config) || config is null)
        {
            return Task.FromResult<RemoteCommandDefinition?>(null);
        }

        if (!_options.Targets.TryGetValue(config.Target, out var target) || target is null)
        {
            throw new Exceptions.RemoteConfigurationException(
                $"Remote operation '{operation}' references an unavailable execution target.");
        }

        return Task.FromResult<RemoteCommandDefinition?>(new RemoteCommandDefinition(
            operation,
            config.Command,
            new RemoteExecutionTargetDefinition(
                config.Target,
                target.Mode,
                target.Host,
                target.Port,
                target.Username,
                target.PrivateKeyFile,
                target.PrivateKeyPassphraseFile,
                target.HostKeyFingerprintSha256),
            config.User,
            config.Arguments.AsReadOnly()));
    }
}
