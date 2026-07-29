using Backoffice.Domain.Recommendations;

namespace Backoffice.Application.Recommendations;

public interface IRecommendationRepository
{
    Task<Recommendation?> FindLatestByCaseAsync(string tenantId, Guid caseId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Recommendation>> ListByCaseAsync(
        string tenantId,
        Guid caseId,
        CancellationToken cancellationToken = default);

    void Add(Recommendation recommendation);
}
