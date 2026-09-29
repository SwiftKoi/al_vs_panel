using AlegacyWebPanel.Core.Errors;

namespace AlegacyWebPanel.Modules.ServerLogs.Exceptions;

public sealed class ServerLogsNotConfiguredException(string serverId)
    : DomainException($"Server log analysis is not configured for server '{serverId}'.");

public sealed class LogEntryNotFoundException(long entryId)
    : DomainException($"Log entry {entryId} was not found.");

public sealed class LogQueryException(string message) : DomainException(message);

public sealed class LogSourceException(string message) : DomainException(message);

public sealed class SavedSearchNotFoundException(long id)
    : DomainException($"Saved search {id} was not found.");
