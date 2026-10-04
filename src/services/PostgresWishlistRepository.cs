using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Services;

public sealed class PostgresWishlistRepository(AppDbContext db) : IWishlistRepository
{
    public async Task UpsertAsync(WishlistItemEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        await using var transaction = await db.Database.BeginTransactionAsync();
        var exists = await db.Wishlists.AnyAsync(item => item.id == entity.id);
        if (exists)
        {
            db.Wishlists.Update(entity);
        }
        else
        {
            db.Wishlists.Add(entity);
        }

        db.WishlistOutboxEvents.Add(new WishlistOutboxEvent
        {
            Id = entity.id,
            ChildId = entity.ChildId,
            Text = entity.Text,
            Type = entity.RequestType,
            DedupeKey = entity.DedupeKey,
            CreatedAt = entity.CreatedAt
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public IAsyncEnumerable<WishlistItemEntity> ListAsync(string childId) =>
        db.Wishlists
            .AsNoTracking()
            .Where(item => item.ChildId == childId)
            .OrderByDescending(item => item.CreatedAt)
            .AsAsyncEnumerable();
}
