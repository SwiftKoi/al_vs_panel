using AlegacyWebPanel.Core.Authorization;
using AlegacyWebPanel.Modules.Users.Contracts;
using AlegacyWebPanel.Modules.Users.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AlegacyWebPanel.Modules.Users.Services;

public sealed class UserService(UserManager<IdentityUser> userManager, ILogger<UserService> logger) : IUserService
{
    public async Task<IEnumerable<UserResponse>> ListUsersAsync(CancellationToken cancellationToken)
    {
        var users = await userManager.Users.ToListAsync(cancellationToken);
        var responses = new List<UserResponse>();

        foreach (var u in users)
        {
            responses.Add(await ToResponseAsync(u));
        }

        return responses;
    }

    public async Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new UserCreationFailedException("Username and password must not be empty.");
        }

        if (!PanelRoles.IsKnown(request.Role))
        {
            throw new InvalidRoleException(request.Role);
        }

        var existing = await userManager.FindByNameAsync(request.Username);
        if (existing is not null)
        {
            throw new UserAlreadyExistsException(request.Username);
        }

        var user = new IdentityUser { UserName = request.Username, Email = request.Username };
        var result = await userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new UserCreationFailedException(errors);
        }

        EnsureSucceeded(await userManager.AddToRoleAsync(user, request.Role));

        logger.LogInformation("User {Username} was created with role {Role}", request.Username, request.Role);
        return new UserResponse(user.Id, user.UserName, false, request.Role);
    }

    public async Task DeleteUserAsync(string userId, string currentUserId, CancellationToken cancellationToken)
    {
        if (userId == currentUserId)
        {
            throw new SelfDeletionException();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            throw new UserNotFoundException(userId);
        }

        await EnsureNotLastAdminAsync(user);

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Could not delete user: {errors}");
        }

        logger.LogInformation("User {UserId} was deleted", userId);
    }

    public async Task<UserResponse> ChangeRoleAsync(
        string userId,
        string role,
        string currentUserId,
        CancellationToken cancellationToken)
    {
        if (!PanelRoles.IsKnown(role))
        {
            throw new InvalidRoleException(role);
        }

        if (userId == currentUserId)
        {
            throw new SelfRoleChangeException();
        }

        var user = await userManager.FindByIdAsync(userId) ?? throw new UserNotFoundException(userId);
        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Count == 1 && currentRoles[0] == role)
        {
            return await ToResponseAsync(user);
        }

        if (role != PanelRoles.Admin)
        {
            await EnsureNotLastAdminAsync(user);
        }

        if (currentRoles.Count > 0)
        {
            EnsureSucceeded(await userManager.RemoveFromRolesAsync(user, currentRoles));
        }

        EnsureSucceeded(await userManager.AddToRoleAsync(user, role));

        // Rotating the stamp makes the user's existing cookie pick up the new role at its next validation.
        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));

        logger.LogInformation("User {UserId} role changed to {Role}", userId, role);
        return await ToResponseAsync(user);
    }

    private async Task EnsureNotLastAdminAsync(IdentityUser user)
    {
        if (!await userManager.IsInRoleAsync(user, PanelRoles.Admin))
        {
            return;
        }

        var admins = await userManager.GetUsersInRoleAsync(PanelRoles.Admin);
        if (admins.Count <= 1)
        {
            throw new LastAdminException();
        }
    }

    private async Task<UserResponse> ToResponseAsync(IdentityUser user)
    {
        var is2fa = await userManager.GetTwoFactorEnabledAsync(user);
        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault(PanelRoles.IsKnown) ?? string.Empty;
        return new UserResponse(user.Id, user.UserName ?? string.Empty, is2fa, role);
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Could not update user roles: {errors}");
        }
    }
}
