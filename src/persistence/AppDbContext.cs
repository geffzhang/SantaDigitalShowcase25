using Microsoft.EntityFrameworkCore;
using Services;

namespace Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WishlistItemEntity> Wishlists => Set<WishlistItemEntity>();
    public DbSet<WishlistOutboxEvent> WishlistOutboxEvents => Set<WishlistOutboxEvent>();
    public DbSet<ProfileSnapshotEntity> ProfileSnapshots => Set<ProfileSnapshotEntity>();
    public DbSet<RecommendationSetEntity> Recommendations => Set<RecommendationSetEntity>();
    public DbSet<LogisticsAssessmentEntity> LogisticsAssessments => Set<LogisticsAssessmentEntity>();
    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
