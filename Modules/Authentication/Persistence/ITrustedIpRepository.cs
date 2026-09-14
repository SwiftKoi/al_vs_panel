namespace AlegacyWebPanel.Modules.Authentication.Persistence;

public interface ITrustedIpRepository
{
    Task<bool> IsIpTrustedAsync(string userId, string ipAddress, CancellationToken cancellationToken);
    Task TrustIpAsync(string userId, string ipAddress, TimeSpan ttl, CancellationToken cancellationToken);
    Task ClearUserTrustedIpsAsync(string userId, CancellationToken cancellationToken);
}
