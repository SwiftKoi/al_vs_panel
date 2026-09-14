using AlegacyWebPanel.Modules.Authentication.Configuration;
using AlegacyWebPanel.Modules.Authentication.Contracts;
using AlegacyWebPanel.Modules.Authentication.Exceptions;
using AlegacyWebPanel.Modules.Authentication.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.Authentication.Services;

public sealed class TwoFactorService(
    UserManager<IdentityUser> userManager,
    ITrustedIpRepository trustedIpRepository,
    IOptions<AuthenticationOptions> options)
    : ITwoFactorService
{
    public async Task<bool> IsTwoFactorEnabledAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user is not null && await userManager.GetTwoFactorEnabledAsync(user);
    }

    public Task<bool> IsIpTrustedAsync(string userId, string ipAddress, CancellationToken cancellationToken)
    {
        return trustedIpRepository.IsIpTrustedAsync(userId, ipAddress, cancellationToken);
    }

    public Task TrustIpAsync(string userId, string ipAddress, CancellationToken cancellationToken)
    {
        var ttlDays = options.Value.TrustedIpTtlDays;
        return trustedIpRepository.TrustIpAsync(userId, ipAddress, TimeSpan.FromDays(ttlDays), cancellationToken);
    }

    public async Task<TwoFactorSetupResponse> GetSetupDetailsAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        var email = await userManager.GetEmailAsync(user) ?? user.UserName ?? "admin";
        var provisioningUri = $"otpauth://totp/AlegacyWebPanel:{email}?secret={key}&issuer=AlegacyWebPanel&digits=6&period=30";

        return new TwoFactorSetupResponse(key!, provisioningUri);
    }

    public async Task EnableTwoFactorAsync(string userId, string code, string clientIp, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        var isCodeValid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            code);

        if (!isCodeValid)
        {
            throw new TwoFactorCodeInvalidException();
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        await TrustIpAsync(userId, clientIp, cancellationToken);
    }

    public async Task DisableTwoFactorAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await trustedIpRepository.ClearUserTrustedIpsAsync(userId, cancellationToken);
    }

    public async Task VerifyTwoFactorCodeAsync(string userId, string code, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        var isCodeValid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            code);

        if (!isCodeValid)
        {
            throw new TwoFactorCodeInvalidException();
        }
    }
}
