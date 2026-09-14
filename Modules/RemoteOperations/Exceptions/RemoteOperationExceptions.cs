using AlegacyWebPanel.Core.Errors;

namespace AlegacyWebPanel.Modules.RemoteOperations.Exceptions;

public sealed class RemoteOperationNotAllowedException(string operation)
    : DomainException($"The remote operation '{operation}' is not allowlisted.");

public sealed class RemoteConfigurationException(string message) : DomainException(message);

public sealed class RemoteOperationFailedException(string operation, int exitStatus, string errorOutput)
    : DomainException($"Remote operation '{operation}' failed with exit code {exitStatus}. Error: {errorOutput}")
{
    public string Operation { get; } = operation;
    public int ExitStatus { get; } = exitStatus;
    public string ErrorOutput { get; } = errorOutput;
}
