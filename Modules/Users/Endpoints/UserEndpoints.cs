using System.Security.Claims;
using AlegacyWebPanel.Core.Errors;
using AlegacyWebPanel.Modules.Users.Contracts;
using AlegacyWebPanel.Modules.Users.Exceptions;
using AlegacyWebPanel.Modules.Users.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace AlegacyWebPanel.Modules.Users.Endpoints;

public static class UserEndpoints
{
    public static async Task<IResult> ListAsync(
        IUserService userService,
        CancellationToken cancellationToken)
    {
        var users = await userService.ListUsersAsync(cancellationToken);
        return Results.Ok(users);
    }

    public static async Task<IResult> CreateAsync(
        CreateUserRequest request,
        IUserService userService,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await userService.CreateUserAsync(request, cancellationToken);
            return Results.Created($"/api/users/{user.Id}", user);
        }
        catch (UserCreationFailedException e)
        {
            throw new HttpException(StatusCodes.Status400BadRequest, "Registration failed", e.Message);
        }
        catch (UserAlreadyExistsException e)
        {
            throw new HttpException(StatusCodes.Status409Conflict, "Conflict", e.Message);
        }
    }

    public static async Task<IResult> DeleteAsync(
        string id,
        IUserService userService,
        UserManager<IdentityUser> userManager,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        var currentUser = await userManager.GetUserAsync(claimsPrincipal);
        if (currentUser is null)
        {
            throw new HttpException(StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication required.");
        }

        try
        {
            await userService.DeleteUserAsync(id, currentUser.Id, cancellationToken);
            return Results.NoContent();
        }
        catch (SelfDeletionException e)
        {
            throw new HttpException(StatusCodes.Status400BadRequest, "Operation failed", e.Message);
        }
        catch (UserNotFoundException e)
        {
            throw new HttpException(StatusCodes.Status404NotFound, "Not Found", e.Message);
        }
    }
}
