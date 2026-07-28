namespace Backoffice.Application.Recommendations;

public sealed class InvestigationNotFoundException(Guid investigationId)
    : Exception($"Investigation '{investigationId}' was not found.")
{
}

/// <summary>
/// Thrown when a case has no evidence at all, so no Recommendation — not even an ABSTAIN —
/// can be created, since contracts/schemas/canonical-models-base.yaml requires
/// evidenceReferences to have at least one item. Maps to 422 Unprocessable Entity.
/// </summary>
public sealed class NoGroundingEvidenceException(Guid caseId)
    : Exception($"Case '{caseId}' has no evidence to ground a recommendation.")
{
}
