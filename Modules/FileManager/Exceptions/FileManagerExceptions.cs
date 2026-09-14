using AlegacyWebPanel.Core.Errors;

namespace AlegacyWebPanel.Modules.FileManager.Exceptions;

public sealed class InstanceNotFoundException(string instanceId)
    : DomainException($"Server instance '{instanceId}' was not found.");

public sealed class RootNotFoundException(string rootId)
    : DomainException($"File manager root '{rootId}' was not found.");

public sealed class InvalidRelativePathException(string message)
    : DomainException(message);

public sealed class PermissionDeniedException(string message)
    : DomainException(message);

public sealed class FileTooLargeException(string message)
    : DomainException(message);

public sealed class UnsupportedFileException(string message)
    : DomainException(message);
