using AlegacyWebPanel.Core.Errors;

namespace AlegacyWebPanel.Modules.Logging.Exceptions;

public sealed class InvalidLogQueryException(string message) : DomainException(message);

public sealed class LogStoreUnavailableException : DomainException
{
    public LogStoreUnavailableException(string message) : base(message)
    {
    }

    public LogStoreUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
