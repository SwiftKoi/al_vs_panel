using AlegacyWebPanel.Modules.Authentication.Contracts;

namespace AlegacyWebPanel.Modules.Authentication.Services;

public interface ITwoFactorService
{
    Task<bool> IsTwoFactorEnabledAsync(string userId, CancellationToken cancellationToken);
    Task<bool> IsIpTrustedAsync(string userId, string ipAddress, CancellationToken cancellationToken);
    Task TrustIpAsync(string userId, string ipAddress, CancellationToken cancellationToken);
    Task<TwoFactorSetupResponse> GetSetupDetailsAsync(string userId, CancellationToken cancellationToken);
    Task EnableTwoFactorAsync(string userId, string code, string clientIp, CancellationToken cancellationToken);
    Task DisableTwoFactorAsync(string userId, CancellationToken cancellationToken);
    Task VerifyTwoFactorCodeAsync(string userId, string code, CancellationToken cancellationToken);
}
