using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Services;

public sealed class PostgresProfileSnapshotRepository(AppDbContext db) : IProfileSnapshotRepository
{
    public async Task StoreAsync(ProfileSnapshotEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        db.ProfileSnapshots.Add(entity);
        await db.SaveChangesAsync();
    }

    public Task<ProfileSnapshotEntity?> GetAsync(string childId, string id) =>
        db.ProfileSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(snapshot => snapshot.ChildId == childId && snapshot.id == id);
}
