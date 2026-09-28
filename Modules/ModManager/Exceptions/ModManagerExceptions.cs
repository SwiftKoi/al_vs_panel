using AlegacyWebPanel.Core.Errors;

namespace AlegacyWebPanel.Modules.ModManager.Exceptions;

public sealed class ModServerNotFoundException(string serverId)
    : DomainException($"Mod management is not configured for server '{serverId}'.");

public sealed class ModNotInstalledException(string modId)
    : DomainException($"Mod '{modId}' is not installed.");

public sealed class ModReleaseNotFoundException(string modId, string version)
    : DomainException($"ModDB has no release {version} of '{modId}'.");

public sealed class ModDbUnavailableException(string message) : DomainException(message);

public sealed class ModValidationException(string message) : DomainException(message);

public sealed class ModUpdateConflictException()
    : DomainException("Another mod update is already running for this server.");

public sealed class ModBackupNotFoundException()
    : DomainException("There is no update to roll back.");

public sealed class ModTargetException(string message) : DomainException(message);
