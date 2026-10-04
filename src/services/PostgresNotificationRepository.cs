using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Services;

public sealed class PostgresNotificationRepository(AppDbContext db) : INotificationRepository
{
    public async Task StoreAsync(NotificationEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        db.Notifications.Add(entity);
        await db.SaveChangesAsync();
    }

    public IAsyncEnumerable<NotificationEntity> ListAsync(string childId, int take = 50) =>
        db.Notifications
            .AsNoTracking()
            .Where(notification => notification.ChildId == childId)
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(take)
            .AsAsyncEnumerable();
}
