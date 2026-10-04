using Microsoft.EntityFrameworkCore;
using Persistence;
using Services;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("elves")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

public sealed class PostgresPersistenceTests(PostgresDatabaseFixture fixture)
    : IClassFixture<PostgresDatabaseFixture>
{
    [Fact]
    public async Task Migrate_CreatesDrasiPublicationForWishlistEvents()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        await db.Database.OpenConnectionAsync();

        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_publication_tables
                    WHERE pubname = 'drasi_wishlist_events'
                      AND schemaname = 'public'
                      AND tablename = 'wishlist_events'
                );
                """;

            Assert.Equal(true, await command.ExecuteScalarAsync());
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task Migrate_EmptyDatabaseAndRoundTripsWishlistOutboxAndNotification()
    {
        var childId = Guid.NewGuid().ToString();
        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();

            db.Wishlists.Add(new WishlistItemEntity
            {
                id = Guid.NewGuid().ToString(),
                ChildId = childId,
                RequestType = "gift",
                Text = "Wind-up train",
                Category = "toys",
                BudgetEstimate = 34.5,
                DedupeKey = Guid.NewGuid().ToString(),
                CreatedAt = UtcTimestamp()
            });
            db.WishlistOutboxEvents.Add(new WishlistOutboxEvent
            {
                Id = Guid.NewGuid().ToString(),
                ChildId = childId,
                Text = "Wind-up train",
                Type = "gift",
                DedupeKey = Guid.NewGuid().ToString(),
                CreatedAt = UtcTimestamp()
            });
            db.Notifications.Add(new NotificationEntity
            {
                id = Guid.NewGuid().ToString(),
                ChildId = childId,
                Type = "wishlist",
                Message = "Wishlist item added",
                RelatedId = null,
                CreatedAt = UtcTimestamp(),
                State = "unread"
            });

            await db.SaveChangesAsync();
        }

        await using var verificationDb = CreateDbContext();
        var wishlist = await verificationDb.Wishlists.SingleAsync(x => x.ChildId == childId);
        var outboxEvent = await verificationDb.WishlistOutboxEvents.SingleAsync(x => x.ChildId == childId);
        var notification = await verificationDb.Notifications.SingleAsync(x => x.ChildId == childId);

        Assert.Equal("Wind-up train", wishlist.Text);
        Assert.Equal("toys", wishlist.Category);
        Assert.Equal(34.5, wishlist.BudgetEstimate);
        Assert.Equal(wishlist.ChildId, outboxEvent.ChildId);
        Assert.Equal("Wind-up train", outboxEvent.Text);
        Assert.Equal("gift", outboxEvent.Type);
        Assert.Equal("wishlist", notification.Type);
        Assert.Equal("Wishlist item added", notification.Message);
    }

    [Fact]
    public async Task Migrate_RoundTripsProfileAndRecommendationJsonFields()
    {
        var profileId = Guid.NewGuid().ToString();
        var recommendationId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
            db.ProfileSnapshots.Add(new ProfileSnapshotEntity
            {
                id = profileId,
                ChildId = childId,
                CreatedAt = UtcTimestamp(),
                Preferences = ["building", "trains"],
                BudgetCeiling = 60,
                BehaviorSummary = "Kind and curious",
                EnrichmentSource = "profile",
                FallbackUsed = false
            });
            db.Recommendations.Add(new RecommendationSetEntity
            {
                id = recommendationId,
                ChildId = childId,
                CreatedAt = UtcTimestamp(),
                ProfileSnapshotId = profileId,
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
            });

            await db.SaveChangesAsync();
        }

        await using var verificationDb = CreateDbContext();
        var profile = await verificationDb.ProfileSnapshots.SingleAsync(x => x.id == profileId);
        var recommendations = await verificationDb.Recommendations.SingleAsync(x => x.id == recommendationId);

        Assert.Collection(
            profile.Preferences,
            preference => Assert.Equal("building", preference),
            preference => Assert.Equal("trains", preference));
        Assert.Equal("Kind and curious", profile.BehaviorSummary);
        Assert.True(recommendations.FallbackUsed);
        Assert.Equal("Wooden train set", Assert.Single(recommendations.Items).Suggestion);
    }

    [Fact]
    public async Task FailedOutboxInsert_RollsBackWishlistInSameTransaction()
    {
        var childId = Guid.NewGuid().ToString();
        var duplicateOutboxId = Guid.NewGuid().ToString();

        await using (var setupDb = CreateDbContext())
        {
            await setupDb.Database.MigrateAsync();
            setupDb.WishlistOutboxEvents.Add(new WishlistOutboxEvent
            {
                Id = duplicateOutboxId,
                ChildId = childId,
                Text = "Existing event",
                Type = "gift",
                DedupeKey = Guid.NewGuid().ToString(),
                CreatedAt = UtcTimestamp()
            });
            await setupDb.SaveChangesAsync();
        }

        var wishlistId = Guid.NewGuid().ToString();
        await using (var db = CreateDbContext())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Wishlists.Add(new WishlistItemEntity
            {
                id = wishlistId,
                ChildId = childId,
                RequestType = "gift",
                Text = "Wind-up train",
                DedupeKey = Guid.NewGuid().ToString(),
                CreatedAt = UtcTimestamp()
            });
            await db.SaveChangesAsync();

            db.WishlistOutboxEvents.Add(new WishlistOutboxEvent
            {
                Id = duplicateOutboxId,
                ChildId = childId,
                Text = "Duplicate event",
                Type = "gift",
                DedupeKey = Guid.NewGuid().ToString(),
                CreatedAt = UtcTimestamp()
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            await transaction.RollbackAsync();
        }

        await using var verificationDb = CreateDbContext();
        Assert.False(await verificationDb.Wishlists.AnyAsync(x => x.id == wishlistId));
    }

    [Fact]
    public async Task DuplicateWishlistId_IsRejectedByDatabase()
    {
        var id = Guid.NewGuid().ToString();

        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
            db.Wishlists.Add(CreateWishlist(id));
            await db.SaveChangesAsync();
        }

        await using var duplicateDb = CreateDbContext();
        duplicateDb.Wishlists.Add(CreateWishlist(id));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateDb.SaveChangesAsync());
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private static WishlistItemEntity CreateWishlist(string id) => new()
    {
        id = id,
        ChildId = Guid.NewGuid().ToString(),
        RequestType = "gift",
        Text = "Train set",
        DedupeKey = Guid.NewGuid().ToString(),
        CreatedAt = UtcTimestamp()
    };

    private static DateTime UtcTimestamp() => new(2026, 10, 4, 12, 30, 0, DateTimeKind.Utc);
}
