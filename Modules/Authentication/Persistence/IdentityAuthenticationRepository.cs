using AlegacyWebPanel.Modules.Authentication.Contracts;
using AlegacyWebPanel.Modules.Users.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AlegacyWebPanel.Modules.Authentication.Persistence;

public sealed class IdentityAuthenticationRepository(
    AppDbContext db,
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager) : IAuthenticationRepository
{
    public Task EnsureCreatedAsync(CancellationToken cancellationToken) => db.Database.MigrateAsync(cancellationToken);

    public async Task<AuthenticatedUser?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByNameAsync(username);
        return user is null ? null : new AuthenticatedUser(user.Id, user.UserName ?? username);
    }

    public async Task<bool> VerifyPasswordAsync(
        AuthenticatedUser user,
        string password,
        CancellationToken cancellationToken)
    {
        var identityUser = await userManager.FindByIdAsync(user.Id);
        if (identityUser is null)
        {
            return false;
        }

        var result = await signInManager.CheckPasswordSignInAsync(identityUser, password, lockoutOnFailure: true);
        return result.Succeeded;
    }

    public async Task CreateAdminAsync(string username, string password, CancellationToken cancellationToken)
    {
        var user = new IdentityUser { UserName = username, Email = username };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Could not create the initial admin user: {errors}");
        }
    }
}
