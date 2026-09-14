using AlegacyWebPanel.Core.Errors;

namespace AlegacyWebPanel.Modules.Authentication.Exceptions;

public sealed class TwoFactorCodeInvalidException()
    : DomainException("The verification code is incorrect.");
