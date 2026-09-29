using AlegacyWebPanel.Modules.Authentication.Contracts;
using AlegacyWebPanel.Modules.Authentication.Exceptions;
using AlegacyWebPanel.Modules.Authentication.Persistence;

namespace AlegacyWebPanel.Modules.Authentication.Services;

public sealed class AuthenticationService(
    IAuthenticationRepository repository,
    ILogger<AuthenticationService> logger)
    : IAuthenticationService
{
    public async Task<AuthenticatedUser> AuthenticateAsync(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Username) || string.IsNullOrWhiteSpace(command.Password))
        {
            throw new AuthenticationFailedException();
        }

        var user = await repository.FindByUsernameAsync(command.Username, cancellationToken);
        if (user is null || !await repository.VerifyPasswordAsync(user, command.Password, cancellationToken))
        {
            logger.LogWarning("Authentication failed for user {Username}", command.Username);
            throw new AuthenticationFailedException();
        }

        logger.LogInformation("User {Username} authenticated", user.Username);
        return user;
        }

    public async Task ChangePasswordAsync(
        string userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
        {
            throw new PasswordChangeFailedException("Both passwords are required.");
        }

        var errors = await repository.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, cancellationToken);
        if (errors.Count > 0)
        {
            throw new PasswordChangeFailedException(string.Join(" ", errors));
        }

        logger.LogInformation("User {UserId} changed their password", userId);
    }
}
