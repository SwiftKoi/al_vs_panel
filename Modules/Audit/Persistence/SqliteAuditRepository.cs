using AlegacyWebPanel.Core.Auditing;
using AlegacyWebPanel.Modules.Audit.Contracts;
using AlegacyWebPanel.Modules.Audit.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AlegacyWebPanel.Modules.Audit.Persistence;

public sealed class SqliteAuditRepository(IDbContextFactory<AuditDbContext> dbFactory) : IAuditRepository
{
    private readonly SemaphoreSlim _initializing = new(1, 1);
    private bool _initialized;

    public Task AddAsync(AuditEvent auditEvent, DateTimeOffset timestamp, CancellationToken cancellationToken) =>
        ExecuteAsync(
            async (db, token) =>
            {
                db.Events.Add(new AuditEventModel
                {
                    TimestampUtc = timestamp.UtcDateTime,
                    Actor = auditEvent.Actor,
                    ActorRole = auditEvent.ActorRole,
                    IpAddress = auditEvent.IpAddress,
                    Category = auditEvent.Category,
                    Action = auditEvent.Action,
                    ServerId = auditEvent.ServerId,
                    Target = auditEvent.Target,
                    DetailsJson = auditEvent.DetailsJson,
                    Succeeded = auditEvent.Succeeded,
                    Error = auditEvent.Error
                });
                await db.SaveChangesAsync(token);
                return true;
            },
            cancellationToken);

    public Task<AuditQueryResult> QueryAsync(AuditQuery query, CancellationToken cancellationToken) =>
        ExecuteAsync(
            async (db, token) =>
            {
                var filtered = ApplyQuery(db.Events.AsNoTracking(), query);
                var total = await filtered.LongCountAsync(token);
                var rows = await filtered
                    .OrderByDescending(e => e.TimestampUtc)
                    .ThenByDescending(e => e.Id)
                    .Skip(query.Offset)
                    .Take(query.Limit)
                    .ToListAsync(token);

                return new AuditQueryResult(rows.Select(ToDto).ToArray(), total);
            },
            cancellationToken);

    public Task<AuditFacets> GetFacetsAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            async (db, token) =>
            {
                var actors = await db.Events.AsNoTracking()
                    .Select(e => e.Actor).Distinct().OrderBy(a => a).ToListAsync(token);
                var actions = await db.Events.AsNoTracking()
                    .Select(e => new { e.Category, e.Action }).Distinct()
                    .OrderBy(a => a.Category).ThenBy(a => a.Action).ToListAsync(token);
                var servers = await db.Events.AsNoTracking()
                    .Where(e => e.ServerId != null).Select(e => e.ServerId!).Distinct().OrderBy(s => s).ToListAsync(token);

                return new AuditFacets(
                    actors,
                    actions.Select(a => new AuditActionFacet(a.Category, a.Action)).ToArray(),
                    servers);
            },
            cancellationToken);

    public Task<long> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) =>
        ExecuteAsync(
            async (db, token) =>
            {
                var cutoff = olderThan.UtcDateTime;
                return (long)await db.Events.Where(e => e.TimestampUtc < cutoff).ExecuteDeleteAsync(token);
            },
            cancellationToken);

    private static IQueryable<AuditEventModel> ApplyQuery(IQueryable<AuditEventModel> source, AuditQuery query)
    {
        var filtered = source;

        if (query.Actor is not null)
        {
            filtered = filtered.Where(e => e.Actor == query.Actor);
        }

        if (query.Category is not null)
        {
            filtered = filtered.Where(e => e.Category == query.Category);
        }

        if (query.Action is not null)
        {
            filtered = filtered.Where(e => e.Action == query.Action);
        }

        if (query.ServerId is not null)
        {
            filtered = filtered.Where(e => e.ServerId == query.ServerId);
        }

        if (query.Succeeded is { } succeeded)
        {
            filtered = filtered.Where(e => e.Succeeded == succeeded);
        }

        if (query.From is { } from)
        {
            var start = from.UtcDateTime;
            filtered = filtered.Where(e => e.TimestampUtc >= start);
        }

        if (query.To is { } to)
        {
            var end = to.UtcDateTime;
            filtered = filtered.Where(e => e.TimestampUtc <= end);
        }

        if (query.Search is { } search)
        {
            filtered = filtered.Where(e =>
                (e.Target != null && e.Target.Contains(search))
                || (e.DetailsJson != null && e.DetailsJson.Contains(search))
                || (e.Error != null && e.Error.Contains(search))
                || e.Actor.Contains(search));
        }

        return filtered;
    }

    private static AuditEntryDto ToDto(AuditEventModel model) =>
        new(
            model.Id,
            new DateTimeOffset(DateTime.SpecifyKind(model.TimestampUtc, DateTimeKind.Utc)),
            model.Actor,
            model.ActorRole,
            model.IpAddress,
            model.Category,
            model.Action,
            model.ServerId,
            model.Target,
            model.DetailsJson,
            model.Succeeded,
            model.Error);

    private async Task<T> ExecuteAsync<T>(
        Func<AuditDbContext, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await EnsureCreatedAsync(db, cancellationToken);
            return await operation(db, cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or IOException
                                              or Microsoft.Data.Sqlite.SqliteException)
        {
            throw new AuditStoreUnavailableException(exception);
        }
    }

    // The database is created on first use, so an event recorded during startup cannot race the schema.
    private async Task EnsureCreatedAsync(AuditDbContext db, CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializing.WaitAsync(cancellationToken);
        try
        {
            if (!_initialized)
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
                _initialized = true;
            }
        }
        finally
        {
            _initializing.Release();
        }
    }
}
