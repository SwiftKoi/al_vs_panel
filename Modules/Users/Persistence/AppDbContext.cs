using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AlegacyWebPanel.Modules.Users.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<UserTrustedIp> UserTrustedIps => Set<UserTrustedIp>();
    public DbSet<LoginEvent> LoginEvents => Set<LoginEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserTrustedIp>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.IpAddress }).IsUnique();
            entity.Property(e => e.IpAddress).IsRequired();

            entity.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LoginEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).IsRequired();
            entity.Property(e => e.IpAddress).IsRequired();
            entity.HasIndex(e => e.OccurredAt);
            entity.HasIndex(e => new { e.Succeeded, e.OccurredAt });
        });
    }
}
