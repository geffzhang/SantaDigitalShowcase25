using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Services;

public sealed class PostgresLogisticsAssessmentRepository(AppDbContext db) : ILogisticsAssessmentRepository
{
    public async Task StoreAsync(LogisticsAssessmentEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        db.LogisticsAssessments.Add(entity);
        await db.SaveChangesAsync();
    }

    public IAsyncEnumerable<LogisticsAssessmentEntity> ListAsync(string childId, int take = 10) =>
        db.LogisticsAssessments
            .AsNoTracking()
            .Where(assessment => assessment.ChildId == childId)
            .OrderByDescending(assessment => assessment.CheckedAt)
            .Take(take)
            .AsAsyncEnumerable();

    public async Task<IReadOnlyList<AssessmentHistoryEntry>> GetAssessmentHistoryAsync(
        string childId,
        string? recommendationSetId = null)
    {
        var assessments = await db.LogisticsAssessments
            .AsNoTracking()
            .Where(assessment =>
                assessment.ChildId == childId &&
                (recommendationSetId == null || assessment.RecommendationSetId == recommendationSetId))
            .OrderByDescending(assessment => assessment.CheckedAt)
            .ToListAsync();

        return assessments
            .SelectMany(assessment => assessment.Items.Select(item => new AssessmentHistoryEntry(
                assessment.id,
                assessment.CheckedAt,
                assessment.RecommendationSetId,
                item.RecommendationItemId,
                item.Feasible,
                item.Reason,
                assessment.OverallStatus,
                assessment.FallbackUsed)))
            .ToList();
    }
}
