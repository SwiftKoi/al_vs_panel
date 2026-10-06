using Microsoft.EntityFrameworkCore;

namespace AlegacyWebPanel.Modules.Audit.Persistence;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditEventModel> Events => Set<AuditEventModel>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<AuditEventModel>(entity =>
        {
            entity.ToTable("AuditEvents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Actor).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ActorRole).HasMaxLength(64).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(64);
            entity.Property(e => e.Category).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ServerId).HasMaxLength(128);
            entity.Property(e => e.Target).HasMaxLength(512);
            entity.Property(e => e.DetailsJson).HasMaxLength(4096);
            entity.Property(e => e.Error).HasMaxLength(512);
            entity.HasIndex(e => new { e.TimestampUtc, e.Id });
            entity.HasIndex(e => new { e.Actor, e.TimestampUtc });
            entity.HasIndex(e => new { e.Category, e.Action, e.TimestampUtc });
        });
    }
}
