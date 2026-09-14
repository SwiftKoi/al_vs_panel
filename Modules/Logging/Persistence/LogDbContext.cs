using Microsoft.EntityFrameworkCore;

namespace AlegacyWebPanel.Modules.Logging.Persistence;

public sealed class LogDbContext(DbContextOptions<LogDbContext> options) : DbContext(options)
{
    public DbSet<LogEntryModel> LogEntries => Set<LogEntryModel>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<LogEntryModel>(entity =>
        {
            entity.ToTable("LogEntries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Level).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Category).HasMaxLength(512).IsRequired();
            entity.Property(e => e.Message).HasMaxLength(8000).IsRequired();
            entity.Property(e => e.StateJson).HasMaxLength(16384);
            entity.Property(e => e.ExceptionType).HasMaxLength(512);
            entity.Property(e => e.ExceptionMessage).HasMaxLength(8000);
            entity.Property(e => e.StackTrace).HasMaxLength(16000);
            entity.HasIndex(e => new { e.TimestampUtc, e.Id });
            entity.HasIndex(e => new { e.Level, e.TimestampUtc });
        });
    }
}
