using Microsoft.EntityFrameworkCore;
using AlegacyWebPanel.Modules.Authentication.Contracts;
using AlegacyWebPanel.Modules.Users.Persistence;

namespace AlegacyWebPanel.Modules.Authentication.Persistence;

public sealed class EfLoginEventRepository(
    AppDbContext db,
    TimeSpan? retentionWindow = null) : ILoginEventRepository
{
    public async Task RecordAsync(LoginEvent entry, CancellationToken cancellationToken)
    {
        if (retentionWindow is { } window)
        {
            var cutoff = DateTime.UtcNow.Add(-window);
            var expired = db.LoginEvents.Where(e => e.OccurredAt < cutoff);
            db.LoginEvents.RemoveRange(expired);
        }

        db.LoginEvents.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        db.LoginEvents.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<LoginEventDto>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        var items = await db.LoginEvents
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip(offset)
            .Take(limit)
            .Select(e => new LoginEventDto(e.Id, e.Username, e.OccurredAt, e.IpAddress, e.Succeeded))
            .ToListAsync(cancellationToken);

        return items;
    }
}
