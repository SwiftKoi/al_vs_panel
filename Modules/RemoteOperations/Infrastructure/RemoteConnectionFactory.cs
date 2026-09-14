using AlegacyWebPanel.Modules.RemoteOperations.Contracts;

namespace AlegacyWebPanel.Modules.RemoteOperations.Infrastructure;

public interface IRemoteConnectionFactory
{
    IRemoteConnection GetConnection(RemoteExecutionTargetDefinition target);
}

public sealed class RemoteConnectionFactory(
    SshRemoteConnection sshConnection,
    LocalRemoteConnection localConnection) : IRemoteConnectionFactory
{
    public IRemoteConnection GetConnection(RemoteExecutionTargetDefinition target) => target.Mode switch
    {
        ExecutionMode.Ssh => sshConnection,
        ExecutionMode.Local => localConnection,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target.Mode, "Unsupported execution mode.")
    };
}
