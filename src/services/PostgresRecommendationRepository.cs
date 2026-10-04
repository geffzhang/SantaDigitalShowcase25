using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Services;

public sealed class PostgresRecommendationRepository(AppDbContext db) : IRecommendationRepository
{
    public async Task StoreAsync(RecommendationSetEntity recommendationSet)
    {
        ArgumentNullException.ThrowIfNull(recommendationSet);
        db.Recommendations.Add(recommendationSet);
        await db.SaveChangesAsync();
    }

    public Task<RecommendationSetEntity?> GetAsync(string childId, string setId) =>
        db.Recommendations
            .AsNoTracking()
            .SingleOrDefaultAsync(set => set.ChildId == childId && set.id == setId);

    public IAsyncEnumerable<RecommendationSetEntity> ListAsync(string childId, int take = 10) =>
        db.Recommendations
            .AsNoTracking()
            .Where(set => set.ChildId == childId)
            .OrderByDescending(set => set.CreatedAt)
            .Take(take)
            .AsAsyncEnumerable();

    public async Task<IReadOnlyList<RationaleAuditEntry>> GetRationaleAuditAsync(string childId, string? setId = null)
    {
        var sets = await db.Recommendations
            .AsNoTracking()
            .Where(set => set.ChildId == childId && (setId == null || set.id == setId))
            .OrderByDescending(set => set.CreatedAt)
            .ToListAsync();

        return sets
            .SelectMany(set => set.Items.Select(item => new RationaleAuditEntry(
                set.id,
                set.CreatedAt,
                item.Id,
                item.Suggestion,
                item.Rationale,
                set.FallbackUsed)))
            .ToList();
    }
}
