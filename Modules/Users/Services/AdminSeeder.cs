using AlegacyWebPanel.Core.Abstractions;
using AlegacyWebPanel.Modules.Users.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.Users.Services;

public sealed class AdminSeeder(
    UserManager<IdentityUser> userManager,
    ISecretReader secretReader,
    IOptions<UsersOptions> options,
    DbContext db)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);

        var username = options.Value.Admin.Username;
        var user = await userManager.FindByNameAsync(username);
        if (user is not null)
        {
            return;
        }

        var passwordPath = options.Value.Admin.PasswordFile;
        var password = secretReader.ReadRequired(passwordPath, "admin password");
        
        var adminUser = new IdentityUser { UserName = username, Email = username };
        var result = await userManager.CreateAsync(adminUser, password.Value);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Could not seed default admin user: {errors}");
        }
    }
}
