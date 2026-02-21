using Microsoft.EntityFrameworkCore;
using TimeSeriesForecast.Core.Models;
using TimeSeriesForecast.Core.Security;

namespace TimeSeriesForecast.Api.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Item> Items => Set<Item>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ItemTag> ItemTags => Set<ItemTag>();

    public DbSet<Series> Series => Set<Series>();
    public DbSet<SeriesPoint> SeriesPoints => Set<SeriesPoint>();
    public DbSet<ForecastRun> ForecastRuns => Set<ForecastRun>();

    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();

    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<DeadLetterMessage> DeadLetterMessages => Set<DeadLetterMessage>();

    public DbSet<FileObject> Files => Set<FileObject>();

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Item
        modelBuilder.Entity<Item>()
            .HasIndex(i => i.Name);
        modelBuilder.Entity<Item>()
            .HasIndex(i => new { i.IsDeleted, i.IsArchived, i.Status });
        modelBuilder.Entity<Item>()
            .Property(i => i.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Item>()
            .HasOne(i => i.Category)
            .WithMany(c => c.Items)
            .HasForeignKey(i => i.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Many-to-many Item <-> Tag
        modelBuilder.Entity<ItemTag>()
            .HasKey(it => new { it.ItemId, it.TagId });
        modelBuilder.Entity<ItemTag>()
            .HasOne(it => it.Item)
            .WithMany(i => i.ItemTags)
            .HasForeignKey(it => it.ItemId);
        modelBuilder.Entity<ItemTag>()
            .HasOne(it => it.Tag)
            .WithMany(t => t.ItemTags)
            .HasForeignKey(it => it.TagId);

        // Series
        modelBuilder.Entity<Series>()
            .HasIndex(s => s.Name);

        modelBuilder.Entity<SeriesPoint>()
            .HasIndex(p => new { p.SeriesId, p.Timestamp });

        modelBuilder.Entity<SeriesPoint>()
            .HasOne(p => p.Series)
            .WithMany(s => s.Points)
            .HasForeignKey(p => p.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ForecastRun>()
            .HasIndex(fr => new { fr.SeriesId, fr.CreatedAt });

        modelBuilder.Entity<ForecastRun>()
            .HasOne(fr => fr.Series)
            .WithMany(s => s.ForecastRuns)
            .HasForeignKey(fr => fr.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        // Idempotency
        modelBuilder.Entity<IdempotencyRecord>()
            .HasIndex(x => new { x.Key, x.Route })
            .IsUnique();

        // Security
        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Role>()
            .HasIndex(r => r.Name)
            .IsUnique();

        modelBuilder.Entity<Permission>()
            .HasIndex(p => p.Name)
            .IsUnique();

        modelBuilder.Entity<UserRole>()
            .HasKey(ur => new { ur.UserId, ur.RoleId });
        modelBuilder.Entity<RolePermission>()
            .HasKey(rp => new { rp.RoleId, rp.PermissionId });

        modelBuilder.Entity<RefreshSession>()
            .HasIndex(s => new { s.UserId, s.Revoked, s.ExpiresAt });
    }
}
