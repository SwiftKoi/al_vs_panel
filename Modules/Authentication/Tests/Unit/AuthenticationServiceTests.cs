using AlegacyWebPanel.Modules.Authentication.Contracts;
using AlegacyWebPanel.Modules.Authentication.Exceptions;
using AlegacyWebPanel.Modules.Authentication.Persistence;
using AlegacyWebPanel.Modules.Authentication.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlegacyWebPanel.Authentication.UnitTests;

public sealed class AuthenticationServiceTests
{
    [Fact]
    public async Task Authenticate_returns_user_when_credentials_are_valid()
    {
        var repository = new FakeAuthenticationRepository
        {
            User = new AuthenticatedUser("1", "admin", "Admin"),
            PasswordIsValid = true
        };
        var service = new AuthenticationService(repository, NullLogger<AuthenticationService>.Instance);

        var result = await service.AuthenticateAsync(new LoginCommand("admin", "correct"), CancellationToken.None);

        Assert.Equal("admin", result.Username);
        Assert.Equal(1, repository.VerifyCalls);
    }

    [Fact]
    public async Task Authenticate_rejects_invalid_credentials()
    {
        var repository = new FakeAuthenticationRepository { PasswordIsValid = false };
        var service = new AuthenticationService(repository, NullLogger<AuthenticationService>.Instance);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            service.AuthenticateAsync(new LoginCommand("admin", "wrong"), CancellationToken.None));
    }

    [Fact]
    public async Task ChangePassword_passes_both_passwords_to_the_repository()
    {
        var repository = new FakeAuthenticationRepository();
        var service = new AuthenticationService(repository, NullLogger<AuthenticationService>.Instance);

        await service.ChangePasswordAsync("1", new ChangePasswordRequest("old", "new"), CancellationToken.None);

        Assert.Equal(("1", "old", "new"), repository.PasswordChange);
    }

    [Fact]
    public async Task ChangePassword_rejects_empty_passwords()
    {
        var repository = new FakeAuthenticationRepository();
        var service = new AuthenticationService(repository, NullLogger<AuthenticationService>.Instance);

        await Assert.ThrowsAsync<PasswordChangeFailedException>(() =>
            service.ChangePasswordAsync("1", new ChangePasswordRequest("old", ""), CancellationToken.None));
        Assert.Null(repository.PasswordChange);
    }

    [Fact]
    public async Task ChangePassword_translates_repository_errors_to_domain_failure()
    {
        var repository = new FakeAuthenticationRepository { PasswordChangeErrors = ["Incorrect password."] };
        var service = new AuthenticationService(repository, NullLogger<AuthenticationService>.Instance);

        var exception = await Assert.ThrowsAsync<PasswordChangeFailedException>(() =>
            service.ChangePasswordAsync("1", new ChangePasswordRequest("wrong", "new"), CancellationToken.None));
        Assert.Contains("Incorrect password.", exception.Message);
    }

    private sealed class FakeAuthenticationRepository : IAuthenticationRepository
    {
        public AuthenticatedUser? User { get; init; }
        public bool PasswordIsValid { get; init; }
        public int VerifyCalls { get; private set; }

        public Task EnsureCreatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AuthenticatedUser?> FindByUsernameAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult(User?.Username == username ? User : null);

        public Task<bool> VerifyPasswordAsync(AuthenticatedUser user, string password, CancellationToken cancellationToken)
        {
            VerifyCalls++;
            return Task.FromResult(PasswordIsValid);
        }

        public Task CreateAdminAsync(string username, string password, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public IReadOnlyList<string> PasswordChangeErrors { get; init; } = [];
        public (string, string, string)? PasswordChange { get; private set; }

        public Task<IReadOnlyList<string>> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken cancellationToken)
        {
            PasswordChange = (userId, currentPassword, newPassword);
            return Task.FromResult(PasswordChangeErrors);
        }
    }
}
