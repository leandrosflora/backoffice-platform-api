using Backoffice.Domain.Recommendations;

namespace Backoffice.Application.Recommendations;

public interface IRecommendationRepository
{
    Task<Recommendation?> FindLatestByCaseAsync(string tenantId, Guid caseId, CancellationToken cancellationToken = default);

    void Add(Recommendation recommendation);
}
