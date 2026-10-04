using Microsoft.EntityFrameworkCore;
using Persistence;
using Services;
using Xunit;

namespace IntegrationTests;

public sealed class SelfHostedWishlistRepositoryTests(PostgresDatabaseFixture fixture)
    : IClassFixture<PostgresDatabaseFixture>
{
    [Fact]
    public async Task UpsertAsync_StoresWishlistAndMatchingOutboxEvent()
    {
        var entity = CreateWishlist("gift", "Wind-up train", null);
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var repository = new PostgresWishlistRepository(db);
        await repository.UpsertAsync(entity);

        await using var verificationDb = CreateDbContext();
        var wishlist = await verificationDb.Wishlists.SingleAsync(x => x.id == entity.id);
        var outboxEvent = await verificationDb.WishlistOutboxEvents.SingleAsync(x => x.Id == entity.id);

        Assert.Equal(entity.ChildId, wishlist.ChildId);
        Assert.Equal(entity.DedupeKey, outboxEvent.DedupeKey);
        Assert.Equal(entity.RequestType, outboxEvent.Type);
        Assert.Equal(entity.Text, outboxEvent.Text);
        Assert.Equal(entity.CreatedAt, outboxEvent.CreatedAt);
    }

    [Fact]
    public async Task UpsertAsync_PersistsBehaviorUpdateEventFields()
    {
        var entity = CreateWishlist("behavior-update", "Shared with a friend", "Nice");
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        await new PostgresWishlistRepository(db).UpsertAsync(entity);

        await using var verificationDb = CreateDbContext();
        var storedWishlist = await verificationDb.Wishlists.SingleAsync(x => x.id == entity.id);
        var outboxEvent = await verificationDb.WishlistOutboxEvents.SingleAsync(x => x.Id == entity.id);

        Assert.Equal("behavior-update", storedWishlist.RequestType);
        Assert.Equal("Nice", storedWishlist.StatusChange);
        Assert.Equal(entity.ChildId, outboxEvent.ChildId);
        Assert.Equal("behavior-update", outboxEvent.Type);
        Assert.Equal("Shared with a friend", outboxEvent.Text);
        Assert.Equal(entity.DedupeKey, outboxEvent.DedupeKey);
    }

    [Fact]
    public async Task UpsertAsync_OutboxFailureRollsBackWishlistRecord()
    {
        var entity = CreateWishlist("gift", "Wind-up train", null);
        await using (var setupDb = CreateDbContext())
        {
            await setupDb.Database.MigrateAsync();
            setupDb.WishlistOutboxEvents.Add(new WishlistOutboxEvent
            {
                Id = entity.id,
                ChildId = entity.ChildId,
                Text = "Existing event",
                Type = "gift",
                DedupeKey = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow
            });
            await setupDb.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            var repository = new PostgresWishlistRepository(db);
            await Assert.ThrowsAsync<DbUpdateException>(() => repository.UpsertAsync(entity));
        }

        await using var verificationDb = CreateDbContext();
        Assert.False(await verificationDb.Wishlists.AnyAsync(x => x.id == entity.id));
    }

    [Fact]
    public async Task ProfileRecommendationAndNotificationRepositories_RoundTripAndFilterByChild()
    {
        var childId = Guid.NewGuid().ToString();
        var otherChildId = Guid.NewGuid().ToString();
        var profile = new ProfileSnapshotEntity
        {
            id = Guid.NewGuid().ToString(),
            ChildId = childId,
            CreatedAt = UtcTimestamp(10),
            Preferences = ["trains", "building"],
            BudgetCeiling = 60,
            BehaviorSummary = "Kind and curious",
            EnrichmentSource = "profile",
            FallbackUsed = false
        };
        var recommendation = new RecommendationSetEntity
        {
            id = Guid.NewGuid().ToString(),
            ChildId = childId,
            CreatedAt = UtcTimestamp(11),
            ProfileSnapshotId = profile.id,
            FallbackUsed = true,
            GenerationSource = "fallback",
            Items =
            [
                new RecommendationItemEntity
                {
                    Id = Guid.NewGuid().ToString(),
                    Suggestion = "Wooden train set",
                    Rationale = "Matches the child's interests",
                    BudgetFit = "within_budget",
                    Availability = "in_stock"
                }
            ]
        };
        var firstNotification = CreateNotification(childId, UtcTimestamp(12), "First");
        var latestNotification = CreateNotification(childId, UtcTimestamp(13), "Latest");
        var otherChildNotification = CreateNotification(otherChildId, UtcTimestamp(14), "Other child");

        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
            await new PostgresProfileSnapshotRepository(db).StoreAsync(profile);
            await new PostgresRecommendationRepository(db).StoreAsync(recommendation);
            var notificationRepository = new PostgresNotificationRepository(db);
            await notificationRepository.StoreAsync(firstNotification);
            await notificationRepository.StoreAsync(latestNotification);
            await notificationRepository.StoreAsync(otherChildNotification);
        }

        await using var verificationDb = CreateDbContext();
        var storedProfile = await new PostgresProfileSnapshotRepository(verificationDb).GetAsync(childId, profile.id);
        var storedRecommendation = await new PostgresRecommendationRepository(verificationDb).GetAsync(childId, recommendation.id);
        var notifications = new List<NotificationEntity>();
        await foreach (var notification in new PostgresNotificationRepository(verificationDb).ListAsync(childId))
        {
            notifications.Add(notification);
        }
        var rationale = await new PostgresRecommendationRepository(verificationDb)
            .GetRationaleAuditAsync(childId, recommendation.id);

        Assert.NotNull(storedProfile);
        Assert.Equal(profile.Preferences, storedProfile.Preferences);
        Assert.Equal("Kind and curious", storedProfile.BehaviorSummary);
        Assert.NotNull(storedRecommendation);
        Assert.Equal("Wooden train set", Assert.Single(storedRecommendation.Items).Suggestion);
        Assert.Collection(
            notifications,
            notification =>
            {
                Assert.Equal("Latest", notification.Message);
                Assert.Equal(childId, notification.ChildId);
            },
            notification =>
            {
                Assert.Equal("First", notification.Message);
                Assert.Equal(childId, notification.ChildId);
            });
        Assert.Equal(recommendation.FallbackUsed, Assert.Single(rationale).FallbackUsed);
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private static WishlistItemEntity CreateWishlist(string requestType, string text, string? statusChange) => new()
    {
        id = Guid.NewGuid().ToString(),
        ChildId = Guid.NewGuid().ToString(),
        RequestType = requestType,
        Text = text,
        Category = "toys",
        BudgetEstimate = 35,
        StatusChange = statusChange,
        DedupeKey = Guid.NewGuid().ToString(),
        CreatedAt = UtcTimestamp(9)
    };

    private static NotificationEntity CreateNotification(string childId, DateTime createdAt, string message) => new()
    {
        id = Guid.NewGuid().ToString(),
        ChildId = childId,
        Type = "wishlist",
        Message = message,
        CreatedAt = createdAt,
        State = "unread"
    };

    private static DateTime UtcTimestamp(int hour) => new(2026, 10, 4, hour, 30, 0, DateTimeKind.Utc);
}
