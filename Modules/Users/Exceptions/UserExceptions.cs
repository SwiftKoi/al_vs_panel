using AlegacyWebPanel.Core.Errors;

namespace AlegacyWebPanel.Modules.Users.Exceptions;

public sealed class UserAlreadyExistsException(string username)
    : DomainException($"A user with the username '{username}' already exists.");

public sealed class UserNotFoundException(string id)
    : DomainException($"User with ID '{id}' was not found.");

public sealed class UserCreationFailedException(string details)
    : DomainException($"Could not create user: {details}");

public sealed class SelfDeletionException()
    : DomainException("You cannot delete your own user account.");
