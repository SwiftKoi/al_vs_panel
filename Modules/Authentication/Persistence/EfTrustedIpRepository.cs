using Microsoft.EntityFrameworkCore;
using AlegacyWebPanel.Modules.Users.Persistence;

namespace AlegacyWebPanel.Modules.Authentication.Persistence;

public sealed class EfTrustedIpRepository(AppDbContext db) : ITrustedIpRepository
{
    public async Task<bool> IsIpTrustedAsync(string userId, string ipAddress, CancellationToken cancellationToken)
    {
        // Inline cleanup of expired entries for the user
        var expired = db.UserTrustedIps.Where(e => e.UserId == userId && e.ExpiresAt <= DateTime.UtcNow);
        db.UserTrustedIps.RemoveRange(expired);

        var exists = await db.UserTrustedIps.AnyAsync(
            e => e.UserId == userId && e.IpAddress == ipAddress && e.ExpiresAt > DateTime.UtcNow,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return exists;
    }

    public async Task TrustIpAsync(string userId, string ipAddress, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var existing = await db.UserTrustedIps.FirstOrDefaultAsync(
            e => e.UserId == userId && e.IpAddress == ipAddress,
            cancellationToken);

        var expiresAt = DateTime.UtcNow.Add(ttl);
        if (existing is not null)
        {
            existing.ExpiresAt = expiresAt;
        }
        else
        {
            db.UserTrustedIps.Add(new UserTrustedIp
            {
                UserId = userId,
                IpAddress = ipAddress,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearUserTrustedIpsAsync(string userId, CancellationToken cancellationToken)
    {
        var entries = db.UserTrustedIps.Where(e => e.UserId == userId);
        db.UserTrustedIps.RemoveRange(entries);
        await db.SaveChangesAsync(cancellationToken);
    }
}
