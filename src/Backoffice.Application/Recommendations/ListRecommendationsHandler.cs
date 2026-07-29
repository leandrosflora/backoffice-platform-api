using Backoffice.Application.Cases;
using Backoffice.Application.Policy;

namespace Backoffice.Application.Recommendations;

public sealed class ListRecommendationsHandler(
    ICaseRepository caseRepository,
    IRecommendationRepository recommendationRepository,
    PolicyEnforcer policyEnforcer)
{
    public async Task<IReadOnlyList<RecommendationResponse>> HandleAsync(
        string tenantId,
        Guid caseId,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.CaseRead,
            new PolicyResource(PolicyResourceTypes.Case, caseId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.CaseProcessing,
            correlationId.ToString(),
            []), cancellationToken: cancellationToken);

        var recommendations = await recommendationRepository.ListByCaseAsync(tenantId, caseId, cancellationToken);
        return recommendations.Select(recommendation => recommendation.ToResponse()).ToList();
    }
}
