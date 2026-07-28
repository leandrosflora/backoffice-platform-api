using Backoffice.Application.Recommendations;
using Backoffice.Domain.Recommendations;
using Backoffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backoffice.Infrastructure.Recommendations;

public sealed class RecommendationRepository(BackofficeDbContext dbContext) : IRecommendationRepository
{
    public async Task<Recommendation?> FindLatestByCaseAsync(string tenantId, Guid caseId, CancellationToken cancellationToken = default)
    {
        // Ordered client-side: SQLite (test/dev provider) cannot translate ORDER BY over
        // DateTimeOffset elsewhere in this codebase, and per-case recommendation counts are
        // small enough that filtering then taking the max version in memory is cheap.
        var recommendations = await dbContext.Recommendations
            .Where(r => r.TenantId == tenantId && r.CaseId == caseId)
            .ToListAsync(cancellationToken);

        return recommendations.OrderByDescending(r => r.RecommendationVersion).FirstOrDefault();
    }

    public void Add(Recommendation recommendation) => dbContext.Recommendations.Add(recommendation);
}
