using System.Security.Claims;
using AlegacyWebPanel.Modules.Authentication.Contracts;
using AlegacyWebPanel.Modules.Authentication.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace AlegacyWebPanel.Modules.Authentication.Infrastructure;

public sealed class IdentityAuthenticationSession(
    SignInManager<IdentityUser> signInManager,
    IHttpContextAccessor httpContextAccessor)
    : IAuthenticationSession
{
    public async Task<AuthenticatedUser?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var user = await signInManager.UserManager.GetUserAsync(principal);
        return user is null ? null : new AuthenticatedUser(user.Id, user.UserName ?? string.Empty);
    }

    public async Task SignInAsync(AuthenticatedUser user, CancellationToken cancellationToken)
    {
        var identityUser = await signInManager.UserManager.FindByIdAsync(user.Id)
                           ?? throw new InvalidOperationException("The authenticated user no longer exists.");
        await signInManager.SignInAsync(identityUser, isPersistent: false);
    }

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var user = await signInManager.UserManager.GetUserAsync(principal);
        if (user is null)
        {
            return false;
        }

        await signInManager.RefreshSignInAsync(user);
        return true;
    }

    public Task SignOutAsync(CancellationToken cancellationToken) => signInManager.SignOutAsync();

    public async Task SignInTwoFactorAsync(AuthenticatedUser user, CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext ?? throw new InvalidOperationException("HttpContext is unavailable.");
        
        var claims = new[] { new Claim(ClaimTypes.Name, user.Id) };
        var identity = new ClaimsIdentity(claims, IdentityConstants.TwoFactorUserIdScheme);
        var principal = new ClaimsPrincipal(identity);

        await context.SignInAsync(IdentityConstants.TwoFactorUserIdScheme, principal);
    }

    public async Task<AuthenticatedUser?> GetTwoFactorUserAsync(CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext ?? throw new InvalidOperationException("HttpContext is unavailable.");
        var result = await context.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        
        if (result.Principal is null)
        {
            return null;
        }

        var userId = result.Principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var user = await signInManager.UserManager.FindByIdAsync(userId);
        return user is null ? null : new AuthenticatedUser(user.Id, user.UserName ?? string.Empty);
    }

    public async Task ClearTwoFactorCookieAsync(CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext ?? throw new InvalidOperationException("HttpContext is unavailable.");
        await context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
    }
}
