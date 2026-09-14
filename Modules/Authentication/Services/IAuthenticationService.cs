using AlegacyWebPanel.Modules.Authentication.Contracts;

namespace AlegacyWebPanel.Modules.Authentication.Services;

/// <summary>
/// Authenticates users by validating credentials against a backing identity store.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Authenticates a user with the provided credentials.
    /// </summary>
    /// <param name="command">The login credentials containing username and password.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ///
    /// <returns>The authenticated user identity.</returns>
    ///
    /// <exception cref="AuthenticationFailedException">Thrown when credentials are invalid.</exception>
    Task<AuthenticatedUser> AuthenticateAsync(LoginCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Manages the authentication session (sign-in / sign-out) for the current HTTP context.
/// </summary>
public interface IAuthenticationSession
{
    /// <summary>Gets the current authenticated user, or null when no session exists.</summary>
    Task<AuthenticatedUser?> GetCurrentUserAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Signs in the user by creating an authentication session cookie.
    /// </summary>
    /// <param name="user">The authenticated user to sign in.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SignInAsync(AuthenticatedUser user, CancellationToken cancellationToken);

    /// <summary>Reissues the current cookie while preserving the current user.</summary>
    Task<bool> RefreshAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Signs out the current user by removing the authentication session cookie.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SignOutAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Signs in the user with a temporary 2FA cookie.
    /// </summary>
    Task SignInTwoFactorAsync(AuthenticatedUser user, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the pending two-factor user, if a 2FA cookie exists.
    /// </summary>
    Task<AuthenticatedUser?> GetTwoFactorUserAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Clears the temporary 2FA cookie.
    /// </summary>
    Task ClearTwoFactorCookieAsync(CancellationToken cancellationToken);
}
