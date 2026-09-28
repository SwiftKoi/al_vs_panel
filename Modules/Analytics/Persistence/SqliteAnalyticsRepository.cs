using AlegacyWebPanel.Modules.Analytics.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AlegacyWebPanel.Modules.Analytics.Persistence;

public sealed class SqliteAnalyticsRepository(IDbContextFactory<AnalyticsDbContext> dbFactory) : IAnalyticsRepository
{
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var script = db.Database.GenerateCreateScript()
            .Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ", StringComparison.Ordinal)
            .Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ", StringComparison.Ordinal)
            .Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ", StringComparison.Ordinal);
        foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }
    }

    public async Task<int> AddEventsAsync(PlayerEventBatch batch, CancellationToken cancellationToken)
    {
        var added = await AddJoinsAsync(batch.Joins, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        foreach (var group in batch.SessionEnds.GroupBy(end => end.ServerId))
        {
            var (from, to) = (group.Min(end => end.OccurredAtUtc), group.Max(end => end.OccurredAtUtc));
            var existing = (await db.SessionEnds.AsNoTracking()
                    .Where(end => end.ServerId == group.Key && end.OccurredAtUtc >= from && end.OccurredAtUtc <= to)
                    .Select(end => new { end.OccurredAtUtc, end.PlayerName })
                    .ToListAsync(cancellationToken))
                .Select(end => $"{AsUtc(end.OccurredAtUtc):O}\n{end.PlayerName}")
                .ToHashSet();
            foreach (var end in group.Where(end => existing.Add($"{end.OccurredAtUtc:O}\n{end.PlayerName}")))
            {
                db.SessionEnds.Add(new SessionEndModel
                {
                    ServerId = end.ServerId,
                    OccurredAtUtc = end.OccurredAtUtc,
                    PlayerName = end.PlayerName,
                    Reason = end.Reason
                });
                added++;
            }
        }

        foreach (var group in batch.Failures.GroupBy(failure => failure.ServerId))
        {
            var (from, to) = (group.Min(failure => failure.OccurredAtUtc), group.Max(failure => failure.OccurredAtUtc));
            var existing = (await db.ConnectionFailures.AsNoTracking()
                    .Where(failure => failure.ServerId == group.Key && failure.OccurredAtUtc >= from && failure.OccurredAtUtc <= to)
                    .Select(failure => new { failure.OccurredAtUtc, failure.RemoteAddress })
                    .ToListAsync(cancellationToken))
                .Select(failure => $"{AsUtc(failure.OccurredAtUtc):O}\n{failure.RemoteAddress}")
                .ToHashSet();
            foreach (var failure in group.Where(failure => existing.Add($"{failure.OccurredAtUtc:O}\n{failure.RemoteAddress}")))
            {
                db.ConnectionFailures.Add(new ConnectionFailureModel
                {
                    ServerId = failure.ServerId,
                    OccurredAtUtc = failure.OccurredAtUtc,
                    RemoteAddress = failure.RemoteAddress,
                    Reason = failure.Reason
                });
                added++;
            }
        }

        foreach (var group in batch.Pauses.GroupBy(pause => pause.ServerId))
        {
            var (from, to) = (group.Min(pause => pause.StartedAtUtc), group.Max(pause => pause.StartedAtUtc));
            var existing = (await db.ServerPauses.AsNoTracking()
                    .Where(pause => pause.ServerId == group.Key && pause.StartedAtUtc >= from && pause.StartedAtUtc <= to)
                    .Select(pause => pause.StartedAtUtc)
                    .ToListAsync(cancellationToken))
                .Select(AsUtc)
                .ToHashSet();
            foreach (var pause in group.Where(pause => existing.Add(pause.StartedAtUtc)))
            {
                db.ServerPauses.Add(new ServerPauseModel
                {
                    ServerId = pause.ServerId,
                    StartedAtUtc = pause.StartedAtUtc,
                    EndedAtUtc = pause.EndedAtUtc
                });
                added++;
            }
        }

        foreach (var group in batch.Overloads.GroupBy(overload => overload.ServerId))
        {
            var (from, to) = (group.Min(o => o.OccurredAtUtc), group.Max(o => o.OccurredAtUtc));
            var existing = (await db.ServerOverloads.AsNoTracking()
                    .Where(o => o.ServerId == group.Key && o.OccurredAtUtc >= from && o.OccurredAtUtc <= to)
                    .Select(o => new { o.OccurredAtUtc, o.TickMs })
                    .ToListAsync(cancellationToken))
                .Select(o => $"{AsUtc(o.OccurredAtUtc):O}\n{o.TickMs}")
                .ToHashSet();
            foreach (var overload in group.Where(o => existing.Add($"{o.OccurredAtUtc:O}\n{o.TickMs}")))
            {
                db.ServerOverloads.Add(new ServerOverloadModel
                {
                    ServerId = overload.ServerId,
                    OccurredAtUtc = overload.OccurredAtUtc,
                    TickMs = overload.TickMs
                });
                added++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    public async Task<int> AddJoinsAsync(IReadOnlyList<PlayerJoin> joins, CancellationToken cancellationToken)
    {
        if (joins.Count == 0)
        {
            return 0;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var added = 0;
        foreach (var group in joins.GroupBy(join => join.ServerId))
        {
            var from = group.Min(join => join.OccurredAtUtc);
            var to = group.Max(join => join.OccurredAtUtc);
            var existing = (await db.PlayerJoins.AsNoTracking()
                    .Where(join => join.ServerId == group.Key && join.OccurredAtUtc >= from && join.OccurredAtUtc <= to)
                    .Select(join => new { join.OccurredAtUtc, join.PlayerName, join.RemoteAddress, join.RemotePort })
                    .ToListAsync(cancellationToken))
                .Select(join => Key(join.OccurredAtUtc, join.PlayerName, join.RemoteAddress, join.RemotePort))
                .ToHashSet();

            foreach (var join in group)
            {
                if (!existing.Add(Key(join.OccurredAtUtc, join.PlayerName, join.RemoteAddress, join.RemotePort)))
                {
                    continue;
                }

                db.PlayerJoins.Add(new PlayerJoinModel
                {
                    ServerId = join.ServerId,
                    OccurredAtUtc = join.OccurredAtUtc,
                    PlayerName = join.PlayerName,
                    RemoteAddress = join.RemoteAddress,
                    RemotePort = join.RemotePort
                });
                added++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    public async Task<IReadOnlyList<ServerOverload>> ListOverloadsAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ServerOverloads.AsNoTracking()
            .Where(o => o.ServerId == serverId && o.OccurredAtUtc >= fromUtc && o.OccurredAtUtc < toUtc)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new ServerOverload(row.ServerId, AsUtc(row.OccurredAtUtc), row.TickMs)).ToArray();
    }

    public async Task AddMetricSampleAsync(ServerMetricSample sample, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.ServerMetricSamples.Add(new ServerMetricSampleModel
        {
            ServerId = sample.ServerId,
            SampledAtUtc = sample.SampledAtUtc,
            CpuPercent = (double)sample.CpuPercent,
            MemoryPercent = (double)sample.MemoryPercent,
            MemoryBytes = sample.MemoryBytes
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ServerMetricSample>> ListMetricSamplesAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ServerMetricSamples.AsNoTracking()
            .Where(sample => sample.ServerId == serverId && sample.SampledAtUtc >= fromUtc && sample.SampledAtUtc < toUtc)
            .OrderBy(sample => sample.SampledAtUtc)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new ServerMetricSample(
                row.ServerId, AsUtc(row.SampledAtUtc), (decimal)row.CpuPercent, (decimal)row.MemoryPercent, row.MemoryBytes))
            .ToArray();
    }

    public async Task AddSamplesAsync(IReadOnlyList<ConnectionSample> samples, CancellationToken cancellationToken)
    {
        if (samples.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.ConnectionSamples.AddRange(samples.Select(sample => new ConnectionSampleModel
        {
            ServerId = sample.ServerId,
            SampledAtUtc = sample.SampledAtUtc,
            PlayerName = sample.PlayerName,
            RemoteAddress = sample.RemoteAddress,
            RemotePort = sample.RemotePort,
            RttMs = (double)sample.RttMs,
            RttVarianceMs = (double)sample.RttVarianceMs,
            RetransmitPercent = (double)sample.RetransmitPercent,
            RetransmitsTotal = sample.RetransmitsTotal,
            SendQueueBytes = sample.SendQueueBytes,
            LastReceiveMs = sample.LastReceiveMs,
            BytesSent = sample.BytesSent,
            BytesReceived = sample.BytesReceived
        }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerJoin>> ListJoinsAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.PlayerJoins.AsNoTracking()
            .Where(join => join.ServerId == serverId && join.OccurredAtUtc >= fromUtc && join.OccurredAtUtc < toUtc)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new PlayerJoin(
                row.ServerId, AsUtc(row.OccurredAtUtc), row.PlayerName, row.RemoteAddress, row.RemotePort))
            .ToArray();
    }

    public async Task<IReadOnlyList<PlayerSessionEnd>> ListSessionEndsAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.SessionEnds.AsNoTracking()
            .Where(end => end.ServerId == serverId && end.OccurredAtUtc >= fromUtc && end.OccurredAtUtc < toUtc)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new PlayerSessionEnd(row.ServerId, AsUtc(row.OccurredAtUtc), row.PlayerName, row.Reason))
            .ToArray();
    }

    public async Task<IReadOnlyList<ConnectionFailure>> ListFailuresAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ConnectionFailures.AsNoTracking()
            .Where(failure => failure.ServerId == serverId && failure.OccurredAtUtc >= fromUtc && failure.OccurredAtUtc < toUtc)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new ConnectionFailure(row.ServerId, AsUtc(row.OccurredAtUtc), row.RemoteAddress, row.Reason))
            .ToArray();
    }

    public async Task<IReadOnlyList<ServerPause>> ListPausesAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ServerPauses.AsNoTracking()
            .Where(pause => pause.ServerId == serverId && pause.StartedAtUtc >= fromUtc && pause.StartedAtUtc < toUtc)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new ServerPause(row.ServerId, AsUtc(row.StartedAtUtc), AsUtc(row.EndedAtUtc)))
            .ToArray();
    }

    public async Task<IReadOnlyList<ConnectionSample>> ListSamplesAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ConnectionSamples.AsNoTracking()
            .Where(sample => sample.ServerId == serverId && sample.SampledAtUtc >= fromUtc && sample.SampledAtUtc < toUtc)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new ConnectionSample(
                row.ServerId, AsUtc(row.SampledAtUtc), row.PlayerName, row.RemoteAddress, row.RemotePort,
                (decimal)row.RttMs, (decimal)row.RttVarianceMs, (decimal)row.RetransmitPercent, row.RetransmitsTotal,
                row.SendQueueBytes, row.LastReceiveMs, row.BytesSent, row.BytesReceived))
            .ToArray();
    }

    public async Task<IReadOnlyDictionary<string, DateTime>> GetFirstJoinsAsync(
        string serverId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.PlayerJoins.AsNoTracking()
            .Where(join => join.ServerId == serverId)
            .GroupBy(join => join.PlayerName)
            .Select(group => new { Name = group.Key, First = group.Min(join => join.OccurredAtUtc) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Name, row => AsUtc(row.First), StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<SampleRoundCount>> ListSampleRoundsAsync(
        string serverId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ConnectionSamples.AsNoTracking()
            .Where(sample => sample.ServerId == serverId && sample.SampledAtUtc >= fromUtc && sample.SampledAtUtc < toUtc)
            .GroupBy(sample => sample.SampledAtUtc)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new SampleRoundCount(AsUtc(row.Key), row.Count)).ToArray();
    }

    public async Task PruneAsync(DateTime samplesBeforeUtc, DateTime eventsBeforeUtc, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.ConnectionSamples.Where(sample => sample.SampledAtUtc < samplesBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);
        await db.ServerMetricSamples.Where(sample => sample.SampledAtUtc < samplesBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);
        await db.PlayerJoins.Where(join => join.OccurredAtUtc < eventsBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);
        await db.SessionEnds.Where(end => end.OccurredAtUtc < eventsBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);
        await db.ConnectionFailures.Where(failure => failure.OccurredAtUtc < eventsBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);
        await db.ServerPauses.Where(pause => pause.StartedAtUtc < eventsBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);
        await db.ServerOverloads.Where(overload => overload.OccurredAtUtc < eventsBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static string Key(DateTime occurredAt, string name, string address, int port) =>
        $"{AsUtc(occurredAt):O}\n{name}\n{address}\n{port}";

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
