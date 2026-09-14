using AlegacyWebPanel.Core.Errors;

namespace AlegacyWebPanel.Modules.Authentication.Exceptions;

public sealed class AuthenticationFailedException()
    : DomainException("The supplied credentials are invalid.");
