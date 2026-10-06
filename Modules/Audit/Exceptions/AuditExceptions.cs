using AlegacyWebPanel.Core.Errors;

namespace AlegacyWebPanel.Modules.Audit.Exceptions;

public sealed class InvalidAuditQueryException(string message) : DomainException(message);

public sealed class AuditStoreUnavailableException(Exception innerException)
    : DomainException("The audit store is unavailable.", innerException);
