using AlegacyWebPanel.Core.Errors;

namespace AlegacyWebPanel.Modules.ServerManagement.Exceptions;

public sealed class ServerNotFoundException(string serverId)
    : DomainException($"Server '{serverId}' was not found.");

public sealed class ServerOperationConflictException(string serverId)
    : DomainException($"Another lifecycle operation is already running for server '{serverId}'.");

public sealed class ServerOperationFailedException(string operation)
    : DomainException($"The configured server operation '{operation}' failed.");

public sealed class ServerUnavailableException(string message) : DomainException(message);

public sealed class InvalidServerCommandException(string message) : DomainException(message);

public sealed class InvalidServerMetricsException()
    : DomainException("The configured metrics operation returned an invalid response.");
