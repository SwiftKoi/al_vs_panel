using Microsoft.EntityFrameworkCore;

namespace AlegacyWebPanel.Modules.Analytics.Persistence;

public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<PlayerJoinModel> PlayerJoins => Set<PlayerJoinModel>();
    public DbSet<ConnectionSampleModel> ConnectionSamples => Set<ConnectionSampleModel>();
    public DbSet<SessionEndModel> SessionEnds => Set<SessionEndModel>();
    public DbSet<ConnectionFailureModel> ConnectionFailures => Set<ConnectionFailureModel>();
    public DbSet<ServerPauseModel> ServerPauses => Set<ServerPauseModel>();
    public DbSet<ServerMetricSampleModel> ServerMetricSamples => Set<ServerMetricSampleModel>();
    public DbSet<ServerOverloadModel> ServerOverloads => Set<ServerOverloadModel>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<PlayerJoinModel>(entity =>
        {
            entity.ToTable("PlayerJoins");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ServerId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.PlayerName).HasMaxLength(128).IsRequired();
            entity.Property(e => e.RemoteAddress).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => new { e.ServerId, e.OccurredAtUtc, e.PlayerName, e.RemoteAddress, e.RemotePort })
                .IsUnique();
            entity.HasIndex(e => new { e.ServerId, e.PlayerName });
        });

        builder.Entity<SessionEndModel>(entity =>
        {
            entity.ToTable("SessionEnds");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ServerId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.PlayerName).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(512);
            entity.HasIndex(e => new { e.ServerId, e.OccurredAtUtc, e.PlayerName }).IsUnique();
        });

        builder.Entity<ConnectionFailureModel>(entity =>
        {
            entity.ToTable("ConnectionFailures");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ServerId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.RemoteAddress).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(512).IsRequired();
            entity.HasIndex(e => new { e.ServerId, e.OccurredAtUtc, e.RemoteAddress }).IsUnique();
        });

        builder.Entity<ServerPauseModel>(entity =>
        {
            entity.ToTable("ServerPauses");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ServerId).HasMaxLength(128).IsRequired();
            entity.HasIndex(e => new { e.ServerId, e.StartedAtUtc }).IsUnique();
        });

        builder.Entity<ServerOverloadModel>(entity =>
        {
            entity.ToTable("ServerOverloads");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ServerId).HasMaxLength(128).IsRequired();
            entity.HasIndex(e => new { e.ServerId, e.OccurredAtUtc, e.TickMs }).IsUnique();
        });

        builder.Entity<ServerMetricSampleModel>(entity =>
        {
            entity.ToTable("ServerMetricSamples");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ServerId).HasMaxLength(128).IsRequired();
            entity.HasIndex(e => new { e.ServerId, e.SampledAtUtc });
        });

        builder.Entity<ConnectionSampleModel>(entity =>
        {
            entity.ToTable("ConnectionSamples");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ServerId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.PlayerName).HasMaxLength(128);
            entity.Property(e => e.RemoteAddress).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => new { e.ServerId, e.SampledAtUtc });
            entity.HasIndex(e => new { e.ServerId, e.PlayerName, e.SampledAtUtc });
        });
    }
}

public sealed class PlayerJoinModel
{
    public long Id { get; set; }
    public string ServerId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
}

public sealed class ConnectionSampleModel
{
    public long Id { get; set; }
    public string ServerId { get; set; } = string.Empty;
    public DateTime SampledAtUtc { get; set; }
    public string? PlayerName { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public double RttMs { get; set; }
    public double RttVarianceMs { get; set; }
    public double RetransmitPercent { get; set; }
    public long RetransmitsTotal { get; set; }
    public long SendQueueBytes { get; set; }
    public long LastReceiveMs { get; set; }
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
}

public sealed class SessionEndModel
{
    public long Id { get; set; }
    public string ServerId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public sealed class ConnectionFailureModel
{
    public long Id { get; set; }
    public string ServerId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class ServerPauseModel
{
    public long Id { get; set; }
    public string ServerId { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
}

public sealed class ServerMetricSampleModel
{
    public long Id { get; set; }
    public string ServerId { get; set; } = string.Empty;
    public DateTime SampledAtUtc { get; set; }
    public double CpuPercent { get; set; }
    public double MemoryPercent { get; set; }
    public long MemoryBytes { get; set; }
}

public sealed class ServerOverloadModel
{
    public long Id { get; set; }
    public string ServerId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public int TickMs { get; set; }
}
