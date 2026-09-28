using AlegacyWebPanel.Core.Errors;

namespace AlegacyWebPanel.Modules.Analytics.Exceptions;

public sealed class AnalyticsServerNotFoundException(string serverId)
    : DomainException($"Server '{serverId}' was not found.");

public sealed class AnalyticsPlayerNotFoundException(string playerName)
    : DomainException($"No connection data was recorded for player '{playerName}'.");

public sealed class InvalidAnalyticsRangeException(string message) : DomainException(message);
