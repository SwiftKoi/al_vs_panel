using AlegacyWebPanel.Modules.Authentication.Contracts;

namespace AlegacyWebPanel.Modules.Authentication.Persistence;

/// <summary>
/// Persists and retrieves authentication-related data (users, password hashes).
/// </summary>
public interface IAuthenticationRepository
{
    /// <summary>
    /// Ensures the underlying authentication store (e.g. database table) is created.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task EnsureCreatedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finds a user by their username.
    /// </summary>
    /// <param name="username">The username to search for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ///
    /// <returns>The matching <see cref="AuthenticatedUser"/>, or null if not found.</returns>
    Task<AuthenticatedUser?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies that the supplied password matches the stored hash for the given user.
    /// </summary>
    /// <param name="user">The user whose password to verify.</param>
    /// <param name="password">The plaintext password to check.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ///
    /// <returns>True if the password is correct; otherwise false.</returns>
    Task<bool> VerifyPasswordAsync(AuthenticatedUser user, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new admin user with the specified credentials.
    /// </summary>
    /// <param name="username">The username for the admin account.</param>
    /// <param name="password">The plaintext password for the admin account.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ///
    /// <exception cref="InvalidOperationException">Thrown when the admin user cannot be created.</exception>
    Task CreateAdminAsync(string username, string password, CancellationToken cancellationToken);
}
