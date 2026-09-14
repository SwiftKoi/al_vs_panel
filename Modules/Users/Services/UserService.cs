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
            var is2fa = await userManager.GetTwoFactorEnabledAsync(u);
            responses.Add(new UserResponse(u.Id, u.UserName ?? string.Empty, is2fa));
        }

        return responses;
    }

    public async Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new UserCreationFailedException("Username and password must not be empty.");
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

        logger.LogInformation("User {Username} was created", request.Username);
        return new UserResponse(user.Id, user.UserName, false);
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

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Could not delete user: {errors}");
        }

        logger.LogInformation("User {UserId} was deleted", userId);
    }
}
