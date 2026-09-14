using AlegacyWebPanel.Core.Errors;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using AlegacyWebPanel.Modules.Authentication.Contracts;
using AlegacyWebPanel.Modules.Authentication.Exceptions;
using AlegacyWebPanel.Modules.Authentication.Services;

namespace AlegacyWebPanel.Modules.Authentication.Endpoints;

public static class AuthenticationEndpoints
{
    public static IResult CsrfAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return Results.Ok(new CsrfTokenResponse(tokens.RequestToken!));
    }

    public static async Task<IResult> SessionAsync(
        IAuthenticationSession session,
        CancellationToken cancellationToken)
    {
        var user = await session.GetCurrentUserAsync(cancellationToken);
        return user is null
            ? throw new HttpException(StatusCodes.Status401Unauthorized, "Authentication required", "No active authentication session exists.")
            : Results.Ok(new AuthenticationSessionResponse(true, user));
    }

    public static async Task<IResult> RefreshAsync(
        IAuthenticationSession session,
        CancellationToken cancellationToken)
    {
        if (!await session.RefreshAsync(cancellationToken))
        {
            throw new HttpException(StatusCodes.Status401Unauthorized, "Authentication required", "No active authentication session exists.");
        }

        var user = await session.GetCurrentUserAsync(cancellationToken);
        return Results.Ok(new AuthenticationSessionResponse(true, user));
    }

    public static async Task<IResult> LoginAsync(
        LoginRequest request,
        IAuthenticationService service,
        ITwoFactorService twoFactorService,
        IAuthenticationSession session,
        ILoginLogService loginLogService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await service.AuthenticateAsync(
                new LoginCommand(request.Username, request.Password),
                cancellationToken);

            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Require 2FA challenge if enabled and IP is not trusted
            if (await twoFactorService.IsTwoFactorEnabledAsync(user.Id, cancellationToken) &&
                !await twoFactorService.IsIpTrustedAsync(user.Id, ipAddress, cancellationToken))
            {
                await session.SignInTwoFactorAsync(user, cancellationToken);
                return Results.Ok(new LoginResponse(
                    Authenticated: false,
                    RequiresTwoFactor: true,
                    User: null));
            }

            // Otherwise direct login and trust the IP
            await twoFactorService.TrustIpAsync(user.Id, ipAddress, cancellationToken);
            await session.SignInAsync(user, cancellationToken);
            await loginLogService.RecordLoginAsync(user.Username, ipAddress, succeeded: true, cancellationToken);

            return Results.Ok(new LoginResponse(
                Authenticated: true,
                RequiresTwoFactor: false,
                User: user));
        }
        catch (AuthenticationFailedException exception)
        {
            await loginLogService.RecordLoginAsync(
                request.Username,
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                succeeded: false,
                cancellationToken);

            throw new HttpException(StatusCodes.Status401Unauthorized, "Authentication failed", exception.Message);
        }
    }

    public static async Task<IResult> LoginTwoFactorAsync(
        TwoFactorLoginRequest request,
        ITwoFactorService twoFactorService,
        IAuthenticationSession session,
        ILoginLogService loginLogService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await session.GetTwoFactorUserAsync(cancellationToken);
        if (user is null)
        {
            throw new HttpException(StatusCodes.Status401Unauthorized, "Two-factor failed", "No pending two-factor authentication session exists.");
        }

        try
        {
            await twoFactorService.VerifyTwoFactorCodeAsync(user.Id, request.Code, cancellationToken);

            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await twoFactorService.TrustIpAsync(user.Id, ipAddress, cancellationToken);

            await session.SignInAsync(user, cancellationToken);
            await session.ClearTwoFactorCookieAsync(cancellationToken);
            await loginLogService.RecordLoginAsync(user.Username, ipAddress, succeeded: true, cancellationToken);

            return Results.Ok(new LoginResponse(
                Authenticated: true,
                RequiresTwoFactor: false,
                User: user));
        }
        catch (TwoFactorCodeInvalidException exception)
        {
            await loginLogService.RecordLoginAsync(
                user.Username,
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                succeeded: false,
                cancellationToken);

            throw new HttpException(StatusCodes.Status401Unauthorized, "Two-factor failed", exception.Message);
        }
    }

    public static async Task<IResult> RecentLoginsAsync(
        ILoginLogService loginLogService,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await loginLogService.GetRecentLoginsAsync(page ?? 1, pageSize ?? 10, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> GetTwoFactorSetupAsync(
        IAuthenticationSession session,
        ITwoFactorService twoFactorService,
        CancellationToken cancellationToken)
    {
        var user = await session.GetCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            throw new HttpException(StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication required.");
        }

        var setup = await twoFactorService.GetSetupDetailsAsync(user.Id, cancellationToken);
        return Results.Ok(setup);
    }

    public static async Task<IResult> EnableTwoFactorAsync(
        EnableTwoFactorRequest request,
        IAuthenticationSession session,
        ITwoFactorService twoFactorService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await session.GetCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            throw new HttpException(StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication required.");
        }

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        try
        {
            await twoFactorService.EnableTwoFactorAsync(user.Id, request.Code, ipAddress, cancellationToken);
            return Results.Ok(new { Success = true });
        }
        catch (TwoFactorCodeInvalidException exception)
        {
            throw new HttpException(StatusCodes.Status400BadRequest, "Verification failed", exception.Message);
        }
    }

    public static async Task<IResult> DisableTwoFactorAsync(
        IAuthenticationSession session,
        ITwoFactorService twoFactorService,
        CancellationToken cancellationToken)
    {
        var user = await session.GetCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            throw new HttpException(StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication required.");
        }

        await twoFactorService.DisableTwoFactorAsync(user.Id, cancellationToken);
        return Results.Ok(new { Success = true });
    }

    public static async Task<IResult> LogoutAsync(
        IAuthenticationSession session,
        CancellationToken cancellationToken)
    {
        await session.SignOutAsync(cancellationToken);
        return Results.NoContent();
    }
}
